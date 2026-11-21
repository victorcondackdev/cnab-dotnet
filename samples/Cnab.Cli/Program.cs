using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Cnab.Cnab240;
using Cnab.Cnab400;
using Cnab.Reconciliation;

namespace Cnab.Cli;

/// <summary>
/// <c>cnab</c>: inspects, validates and reconciles FEBRABAN CNAB files. A thin
/// sample on top of the library, with the same commands and output as the Go
/// command-line tool.
/// </summary>
/// <remarks>
/// Exit status is 0 on success, 1 when a file is invalid (or, with
/// <c>--strict</c>, when the reconciliation needs attention) and 2 on usage errors.
/// </remarks>
public static class Program
{
    private const string Usage = """
        cnab inspects, validates and reconciles FEBRABAN CNAB files.

        Usage:
          cnab inspect [--json] FILE
          cnab validate [--totals] FILE...
          cnab reconcile --expected expected.csv [--grace-days N] [--strict] [--json] RETORNO

        """;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.Write(Usage);
            return 2;
        }

        string[] rest = args[1..];
        switch (args[0])
        {
            case "inspect":
                return Inspect(rest, stdout, stderr);
            case "validate":
                return Validate(rest, stdout, stderr);
            case "reconcile":
                return Reconcile(rest, stdout, stderr);
            case "help":
            case "-h":
            case "--help":
                stdout.Write(Usage);
                return 0;
            default:
                stderr.Write("cnab: unknown command \"" + args[0] + "\"\n\n" + Usage);
                return 2;
        }
    }

    /// <summary>Parsed command-line options: boolean flags, valued flags and positional arguments.</summary>
    private sealed class Options
    {
        public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public List<string> Positional { get; } = new();

        /// <summary>
        /// Parses <c>--flag</c>, <c>--name value</c> and <c>--name=value</c>
        /// (one or two dashes) up to the first positional argument, like Go's
        /// flag package. Returns an error message for unknown flags.
        /// </summary>
        public static string? TryParse(string[] args, ISet<string> booleans, ISet<string> valued, out Options options)
        {
            options = new Options();
            int i = 0;
            for (; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--")
                {
                    i++;
                    break;
                }

                if (a.Length < 2 || a[0] != '-')
                {
                    break;
                }

                string name = a.TrimStart('-');
                string? value = null;
                int eq = name.IndexOf('=', StringComparison.Ordinal);
                if (eq >= 0)
                {
                    value = name.Substring(eq + 1);
                    name = name.Substring(0, eq);
                }

                if (booleans.Contains(name) && value is null)
                {
                    options.Flags.Add(name);
                }
                else if (valued.Contains(name))
                {
                    if (value is null)
                    {
                        if (i + 1 >= args.Length)
                        {
                            return "flag needs an argument: -" + name;
                        }

                        value = args[++i];
                    }

                    options.Values[name] = value;
                }
                else
                {
                    return "flag provided but not defined: -" + name;
                }
            }

            options.Positional.AddRange(args[i..]);
            return null;
        }
    }

    private static string Date(DateOnly? d) => d is DateOnly x ? x.ToString("yyyy-MM-dd", Inv) : string.Empty;

    // --- inspect -------------------------------------------------------------

    private sealed class BatchSummary
    {
        public int Lote { get; init; }

        public string Operacao { get; init; } = string.Empty;

        public int Records { get; init; }

        public SortedDictionary<string, int> Segments { get; } = new(StringComparer.Ordinal);

        public int Titles { get; set; }

        public long Nominal { get; set; }

        public long Paid { get; set; }

        public long Credited { get; set; }

        public long Tariffs { get; set; }

        public SortedDictionary<string, int> Movimentos { get; } = new(StringComparer.Ordinal);
    }

    private sealed class FileSummary
    {
        public string Format { get; init; } = string.Empty;

        public string Banco { get; init; } = string.Empty;

        public string Empresa { get; init; } = string.Empty;

        public string Tipo { get; init; } = string.Empty;

        public string Gerado { get; init; } = string.Empty;

        public int Nsa { get; init; }

        public int Lines { get; init; }

        public List<BatchSummary> Batches { get; } = new();

        public int Details { get; init; }
    }

    private static void Increment(SortedDictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;

    private static FileSummary Summarize(Cnab240File f)
    {
        string tipo = f.Header.CodigoRemessaRetorno switch
        {
            CobrancaCodes.CodigoRemessa => "remessa",
            CobrancaCodes.CodigoRetorno => "retorno",
            int other => "código " + other.ToString(Inv),
        };
        var s = new FileSummary
        {
            Format = "CNAB 240",
            Banco = f.Header.Banco,
            Empresa = f.Header.NomeEmpresa,
            Tipo = tipo,
            Gerado = Date(f.Header.DataGeracao),
            Nsa = f.Header.NSA,
            Lines = f.Trailer.QuantidadeRegistros,
        };
        foreach (Batch b in f.Batches)
        {
            var bs = new BatchSummary { Lote = b.Header.Lote, Operacao = b.Header.Operacao, Records = b.Segments.Count + 2 };
            foreach (Segment seg in b.Segments)
            {
                Increment(bs.Segments, seg.Code == (char)0 ? "raw" : new string(seg.Code, 1));
                switch (seg)
                {
                    case SegmentP p:
                        bs.Titles++;
                        bs.Nominal += p.Valor;
                        Increment(bs.Movimentos, p.Movimento.Label());
                        break;
                    case SegmentT t:
                        bs.Titles++;
                        bs.Nominal += t.Valor;
                        bs.Tariffs += t.Tarifa;
                        Increment(bs.Movimentos, t.Movimento.Label());
                        break;
                    case SegmentU u when u.Movimento.IsLiquidacao():
                        bs.Paid += u.ValorPago;
                        bs.Credited += u.ValorLiquido;
                        break;
                }
            }

            s.Batches.Add(bs);
        }

        return s;
    }

    private static int Inspect(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? error = Options.TryParse(args, new HashSet<string> { "json" }, new HashSet<string>(), out Options o);
        if (error is not null || o.Positional.Count != 1)
        {
            stderr.WriteLine(error ?? "usage: cnab inspect [--json] FILE");
            return 2;
        }

        FileSummary s;
        try
        {
            byte[] data = File.ReadAllBytes(o.Positional[0]);
            if (CnabFile.Detect(data) == FileLayout.Cnab400)
            {
                Cnab400File f = CnabFile.Parse400(new MemoryStream(data));
                s = new FileSummary
                {
                    Format = "CNAB 400",
                    Banco = f.Header.Banco,
                    Empresa = f.Header.NomeEmpresa,
                    Tipo = f.Header.LiteralOperacao.ToLowerInvariant(),
                    Gerado = Date(f.Header.DataGravacao),
                    Lines = f.Details.Count + 2,
                    Details = f.Details.Count,
                };
            }
            else
            {
                s = Summarize(CnabFile.Parse(new MemoryStream(data)));
            }
        }
        catch (Exception e) when (e is CnabException or IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine("cnab: " + e.Message);
            return 1;
        }

        if (o.Flags.Contains("json"))
        {
            WriteJson(stdout, w => InspectJson(w, s));
            return 0;
        }

        stdout.Write(s.Format + " " + s.Tipo + ", banco " + s.Banco + ", empresa " + s.Empresa + "\n");
        stdout.Write("gerado em " + s.Gerado + ", " + (s.Nsa > 0 ? "NSA " + s.Nsa.ToString(Inv) + ", " : string.Empty) + s.Lines.ToString(Inv) + " lines\n");
        if (s.Format == "CNAB 400")
        {
            stdout.Write(s.Details.ToString(Inv) + " detail records (decode with a bank-specific layout)\n");
            return 0;
        }

        var rows = new List<string[]> { new[] { "lote", "op", "records", "segments", "titles", "nominal", "paid", "credited", "tariffs" } };
        foreach (BatchSummary b in s.Batches)
        {
            rows.Add(new[]
            {
                b.Lote.ToString("D4", Inv), b.Operacao, b.Records.ToString(Inv),
                string.Join(" ", b.Segments.Select(kv => kv.Key + ":" + kv.Value.ToString(Inv))),
                b.Titles.ToString(Inv), Money.Format(b.Nominal), Money.Format(b.Paid), Money.Format(b.Credited), Money.Format(b.Tariffs),
            });
        }

        // Right-aligned columns with two spaces of padding, like Go's text/tabwriter.
        int[] widths = Enumerable.Range(0, rows[0].Length).Select(c => rows.Max(r => r[c].Length) + 2).ToArray();
        foreach (string[] row in rows)
        {
            stdout.Write(string.Concat(row.Select((cell, c) => cell.PadLeft(widths[c]))) + "\n");
        }

        foreach (BatchSummary b in s.Batches)
        {
            stdout.Write("lote " + b.Lote.ToString("D4", Inv) + " movimentos:\n");
            foreach (KeyValuePair<string, int> m in b.Movimentos)
            {
                stdout.Write("  " + m.Value.ToString(Inv).PadLeft(4) + "  " + m.Key + "\n");
            }
        }

        return 0;
    }

    private static void InspectJson(Utf8JsonWriter w, FileSummary s)
    {
        w.WriteStartObject();
        w.WriteString("format", s.Format);
        w.WriteString("banco", s.Banco);
        w.WriteString("empresa", s.Empresa);
        w.WriteString("tipo", s.Tipo);
        if (s.Gerado.Length > 0)
        {
            w.WriteString("gerado_em", s.Gerado);
        }

        if (s.Nsa != 0)
        {
            w.WriteNumber("nsa", s.Nsa);
        }

        w.WriteNumber("lines", s.Lines);
        if (s.Batches.Count > 0)
        {
            w.WriteStartArray("batches");
            foreach (BatchSummary b in s.Batches)
            {
                w.WriteStartObject();
                w.WriteNumber("lote", b.Lote);
                w.WriteString("operacao", b.Operacao);
                w.WriteNumber("records", b.Records);
                WriteCounts(w, "segments", b.Segments);
                w.WriteNumber("titles", b.Titles);
                w.WriteNumber("nominal_cents", b.Nominal);
                w.WriteNumber("paid_cents", b.Paid);
                w.WriteNumber("credited_cents", b.Credited);
                w.WriteNumber("tariffs_cents", b.Tariffs);
                WriteCounts(w, "movimentos", b.Movimentos);
                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        if (s.Details != 0)
        {
            w.WriteNumber("details", s.Details);
        }

        w.WriteEndObject();
    }

    private static void WriteCounts(Utf8JsonWriter w, string name, IEnumerable<KeyValuePair<string, int>> counts)
    {
        w.WriteStartObject(name);
        foreach (KeyValuePair<string, int> kv in counts)
        {
            w.WriteNumber(kv.Key, kv.Value);
        }

        w.WriteEndObject();
    }

    private static void WriteJson(TextWriter stdout, Action<Utf8JsonWriter> write)
    {
        var buffer = new MemoryStream();
        var options = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (var w = new Utf8JsonWriter(buffer, options))
        {
            write(w);
        }

        stdout.Write(Encoding.UTF8.GetString(buffer.ToArray()) + "\n");
    }

    // --- validate ------------------------------------------------------------

    private static int Validate(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? error = Options.TryParse(args, new HashSet<string> { "totals" }, new HashSet<string>(), out Options o);
        if (error is not null || o.Positional.Count == 0)
        {
            stderr.WriteLine(error ?? "usage: cnab validate [--totals] FILE...");
            return 2;
        }

        var parseOptions = new ParseOptions { CheckTotals = o.Flags.Contains("totals") };
        int status = 0;
        foreach (string name in o.Positional)
        {
            try
            {
                byte[] data = File.ReadAllBytes(name);
                FileLayout layout = CnabFile.Detect(data);
                if (layout == FileLayout.Cnab400)
                {
                    CnabFile.Parse400(new MemoryStream(data));
                }
                else
                {
                    CnabFile.Parse(new MemoryStream(data), parseOptions);
                }

                stdout.Write("ok   " + name + " (" + layout.Describe() + ")\n");
            }
            catch (Exception e) when (e is CnabException or IOException or UnauthorizedAccessException)
            {
                stdout.Write("FAIL " + name + ": " + e.Message + "\n");
                status = 1;
            }
        }

        return status;
    }

    // --- reconcile -----------------------------------------------------------

    private static int Reconcile(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? error = Options.TryParse(
            args,
            new HashSet<string> { "strict", "json" },
            new HashSet<string> { "expected", "grace-days" },
            out Options o);
        int grace = 0;
        if (error is null && o.Values.TryGetValue("grace-days", out string? g) && !int.TryParse(g, NumberStyles.Integer, Inv, out grace))
        {
            error = "invalid value \"" + g + "\" for flag -grace-days";
        }

        if (error is not null || !o.Values.ContainsKey("expected") || o.Positional.Count != 1)
        {
            stderr.WriteLine(error ?? "usage: cnab reconcile --expected expected.csv [--grace-days N] [--strict] [--json] RETORNO");
            return 2;
        }

        ReconcileReport report;
        try
        {
            List<ExpectedBoleto> expected = ExpectedCsv.Read(File.ReadAllBytes(o.Values["expected"]));
            Cnab240File retorno;
            using (FileStream stream = File.OpenRead(o.Positional[0]))
            {
                retorno = CnabFile.Parse(stream);
            }

            report = Reconciler.Reconcile(expected, Reconciler.EntriesFrom(retorno), new ReconcileOptions { GraceDays = grace });
        }
        catch (Exception e) when (e is CnabException or IOException or UnauthorizedAccessException or FormatException)
        {
            stderr.WriteLine("cnab: " + e.Message);
            return 1;
        }

        if (o.Flags.Contains("json"))
        {
            WriteJson(stdout, w => ReportJson(w, report));
        }
        else
        {
            stdout.Write(report.Summary());
        }

        return o.Flags.Contains("strict") && report.NeedsAttention ? 1 : 0;
    }

    private static void ReportJson(Utf8JsonWriter w, ReconcileReport report)
    {
        w.WriteStartObject();
        w.WriteNumber("expected_total_cents", report.ExpectedTotal);
        w.WriteNumber("paid_total_cents", report.PaidTotal);
        w.WriteNumber("credited_total_cents", report.CreditedTotal);
        w.WriteNumber("tariff_total_cents", report.TariffTotal);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (ReconcileResult r in report.Results)
        {
            Increment(counts, r.Status.ToSnakeCase());
        }

        WriteCounts(w, "counts", counts);
        w.WriteBoolean("needs_attention", report.NeedsAttention);
        w.WriteStartArray("results");
        foreach (ReconcileResult r in report.Results)
        {
            w.WriteStartObject();
            w.WriteString("status", r.Status.ToSnakeCase());
            w.WriteString("nosso_numero", r.NossoNumero);
            if (r.Expected is { } e)
            {
                if (e.Referencia.Length > 0)
                {
                    w.WriteString("referencia", e.Referencia);
                }
            }

            if (r.Entry is { } en)
            {
                w.WriteString("movimento", en.Movimento.Label());
            }

            if (r.Expected is { } exp)
            {
                w.WriteNumber("expected_cents", exp.Valor);
            }

            if (r.Entry is { } paid && paid.Movimento.IsLiquidacao())
            {
                w.WriteNumber("paid_cents", paid.ValorPago);
            }

            w.WriteNumber("difference_cents", r.Difference);
            if (r.Expected?.Vencimento is DateOnly venc)
            {
                w.WriteString("vencimento", Date(venc));
            }

            if (r.Entry is { } entry)
            {
                if (entry.DataOcorrencia is not null)
                {
                    w.WriteString("data_ocorrencia", Date(entry.DataOcorrencia));
                }

                if (entry.DataCredito is not null)
                {
                    w.WriteString("data_credito", Date(entry.DataCredito));
                }

                if (entry.Motivos.Count > 0)
                {
                    w.WriteStartArray("motivos");
                    foreach (string m in entry.Motivos)
                    {
                        w.WriteStringValue(m);
                    }

                    w.WriteEndArray();
                }
            }

            w.WriteString("explanation", r.Explanation);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }
}

/// <summary>
/// Reads the expected-boletos CSV: a header row with <c>nosso_numero</c>,
/// <c>valor</c> (<c>1234.56</c> or <c>1.234,56</c>) and <c>vencimento</c>
/// (<c>YYYY-MM-DD</c>, <c>DD/MM/YYYY</c> or <c>DDMMYYYY</c>), plus an optional
/// <c>referencia</c>. Comma and semicolon delimiters are detected.
/// </summary>
internal static class ExpectedCsv
{
    public static List<ExpectedBoleto> Read(byte[] data)
    {
        string text = Encoding.UTF8.GetString(data);
        if (text.Length > 0 && text[0] == (char)0xFEFF)
        {
            text = text.Substring(1); // UTF-8 BOM from spreadsheets
        }

        string firstLine = text.Split('\n')[0];
        char delimiter = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
        List<string[]> rows = Parse(text, delimiter);
        if (rows.Count == 0)
        {
            throw new FormatException("expected CSV is empty");
        }

        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < rows[0].Length; i++)
        {
            columns[rows[0][i].Trim().ToLowerInvariant()] = i;
        }

        foreach (string required in new[] { "nosso_numero", "valor", "vencimento" })
        {
            if (!columns.ContainsKey(required))
            {
                throw new FormatException("expected CSV: missing column \"" + required + "\"");
            }
        }

        bool hasReferencia = columns.TryGetValue("referencia", out int referencia);
        var expected = new List<ExpectedBoleto>(rows.Count - 1);
        for (int r = 1; r < rows.Count; r++)
        {
            string[] row = rows[r];
            string line = (r + 1).ToString(CultureInfo.InvariantCulture);
            string valor = row[columns["valor"]];
            if (!Money.TryParse(valor, out long cents))
            {
                throw new FormatException("expected CSV line " + line + ": valor \"" + valor + "\": money: invalid amount");
            }

            string venc = row[columns["vencimento"]];
            if (!TryParseDate(venc, out DateOnly? vencimento))
            {
                throw new FormatException("expected CSV line " + line + ": vencimento \"" + venc + "\": want YYYY-MM-DD or DD/MM/YYYY");
            }

            expected.Add(new ExpectedBoleto
            {
                NossoNumero = row[columns["nosso_numero"]].Trim(),
                Valor = cents,
                Vencimento = vencimento,
                Referencia = hasReferencia ? row[referencia] : string.Empty,
            });
        }

        return expected;
    }

    private static bool TryParseDate(string s, out DateOnly? date)
    {
        date = null;
        s = s.Trim();
        if (s.Length == 0)
        {
            return true;
        }

        foreach (string format in new[] { "yyyy-MM-dd", "dd/MM/yyyy", "ddMMyyyy" })
        {
            if (DateOnly.TryParseExact(s, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly d))
            {
                date = d;
                return true;
            }
        }

        return false;
    }

    /// <summary>Splits CSV text into rows: quoted fields with doubled quotes, leading spaces trimmed, blank lines skipped.</summary>
    private static List<string[]> Parse(string text, char delimiter)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        int i = 0;
        int lineNumber = 1;
        while (i <= text.Length)
        {
            // Start of a field.
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
            {
                i++;
            }

            field.Clear();
            if (i < text.Length && text[i] == '"')
            {
                i++;
                while (true)
                {
                    if (i >= text.Length)
                    {
                        throw new FormatException("expected CSV line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": extraneous or missing \" in quoted-field");
                    }

                    char c = text[i++];
                    if (c == '"')
                    {
                        if (i < text.Length && text[i] == '"')
                        {
                            field.Append('"');
                            i++;
                            continue;
                        }

                        break;
                    }

                    if (c == '\n')
                    {
                        lineNumber++;
                    }

                    field.Append(c);
                }

                if (i < text.Length && text[i] != delimiter && text[i] != '\n' && text[i] != '\r')
                {
                    throw new FormatException("expected CSV line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": extraneous or missing \" in quoted-field");
                }
            }
            else
            {
                while (i < text.Length && text[i] != delimiter && text[i] != '\n')
                {
                    if (text[i] == '"')
                    {
                        throw new FormatException("expected CSV line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": bare \" in non-quoted-field");
                    }

                    field.Append(text[i++]);
                }

                if (field.Length > 0 && field[^1] == '\r')
                {
                    field.Length--;
                }
            }

            fields.Add(field.ToString());
            if (i < text.Length && text[i] == delimiter)
            {
                i++;
                continue;
            }

            // End of record (newline, CRLF or end of input).
            if (i < text.Length && text[i] == '\r')
            {
                i++;
            }

            bool blank = fields.Count == 1 && fields[0].Length == 0;
            if (!blank)
            {
                if (rows.Count > 0 && fields.Count != rows[0].Length)
                {
                    throw new FormatException("expected CSV line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": wrong number of fields");
                }

                rows.Add(fields.ToArray());
            }

            fields.Clear();
            lineNumber++;
            i++; // skip the newline, or step past the end of input
        }

        return rows;
    }
}
