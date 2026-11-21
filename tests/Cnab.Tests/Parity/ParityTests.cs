using System.Globalization;
using System.Text;
using Cnab.Cnab240;
using Cnab.Cnab400;
using Cnab.Cnab400.Bradesco;
using Cnab.Reconciliation;
using Cnab.Remessa;

namespace Cnab.Tests.Parity;

/// <summary>
/// Cross-language parity: every vector in <see cref="GoVectors"/> was produced
/// by running the Go implementation (github.com/victorcondackdev/cnab-go) and
/// the C# port must produce identical results: the same layouts, the same
/// decoded values, byte-identical written files (compared by SHA-256), and
/// the same error messages and reconciliation reports.
/// </summary>
public class ParityTests
{
    private static readonly DateTime GeneratedAt = new(2026, 11, 20, 9, 30, 15);

    /// <summary>The record of the Go fixedwidth unit tests (tools/vecgen declares the same layout).</summary>
    public sealed record Rec
    {
        [CnabField(1, 1, CnabFormat.Const, Value = "3")]
        public string Tipo { get; set; } = "3";

        [CnabField(2, 3, CnabFormat.Num)]
        public string Codigo { get; set; } = string.Empty;

        [CnabField(5, 3, CnabFormat.Num)]
        public int Qtd { get; set; }

        [CnabField(8, 10, CnabFormat.Alfa)]
        public string Nome { get; set; } = string.Empty;

        [CnabField(18, 8, CnabFormat.Date)]
        public DateOnly? Data { get; set; }

        [CnabField(26, 6, CnabFormat.Date)]
        public DateOnly? Data6 { get; set; }

        [CnabField(32, 10, CnabFormat.Money)]
        public long Valor { get; set; }

        [CnabField(42, 4, CnabFormat.Raw)]
        public string Livre { get; set; } = string.Empty;

        [CnabField(46, 2, CnabFormat.Num)]
        public byte Small { get; set; }
    }

    [Fact]
    public void LayoutsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string type, string fields) in GoVectors.Layouts)
        {
            IReadOnlyList<CnabFieldInfo> actual = type switch
            {
                "FileHeader" => FixedWidth.Fields<FileHeader>(),
                "BatchHeader" => FixedWidth.Fields<BatchHeader>(),
                "SegmentP" => FixedWidth.Fields<SegmentP>(),
                "SegmentQ" => FixedWidth.Fields<SegmentQ>(),
                "SegmentR" => FixedWidth.Fields<SegmentR>(),
                "SegmentT" => FixedWidth.Fields<SegmentT>(),
                "SegmentU" => FixedWidth.Fields<SegmentU>(),
                "BatchTrailer" => FixedWidth.Fields<BatchTrailer>(),
                "FileTrailer" => FixedWidth.Fields<FileTrailer>(),
                "Header400" => FixedWidth.Fields<Cnab400.Header>(),
                "Detail400" => FixedWidth.Fields<Detail>(),
                "Trailer400" => FixedWidth.Fields<Trailer>(),
                "DetalheRemessa" => FixedWidth.Fields<DetalheRemessa>(),
                _ => throw new InvalidOperationException(type),
            };
            string rendered = string.Join(",", actual.Select(f =>
                f.Name + ":" + f.Start.ToString(CultureInfo.InvariantCulture) + "-" + f.End.ToString(CultureInfo.InvariantCulture)
                + ":" + f.Format.ToString().ToLowerInvariant() + (f.Const.Length > 0 ? ":" + f.Const : string.Empty)));
            Check(failures, type, fields, rendered);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void SampleFilesMatchGo()
    {
        var failures = new List<string>();
        foreach (var v in GoVectors.Files240)
        {
            byte[] data = TestData.Bytes(v.Name);
            Check(failures, v.Name + " input", v.Input, "ok|" + TestData.Sha(data));
            Cnab240File file = CnabFile.Parse(new MemoryStream(data));
            Check(failures, v.Name + " dump", v.Dump, TestData.FileDump(file));
            Check(failures, v.Name + " write", v.Write, Write(file, CnabWriter.Default));

            string lf = Encoding.Latin1.GetString(data).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
            Cnab240File lfFile = CnabFile.Parse(new StringReader(lf));
            Check(failures, v.Name + " write lf", v.WriteLf, Write(lfFile, new CnabWriter { LineEnding = "\n", OmitFinalNewline = true }));
            Check(failures, v.Name + " totals", v.Totals, Capture(() => CnabFile.Parse(new MemoryStream(data), new ParseOptions { CheckTotals = true })));
        }

        foreach (var v in GoVectors.Files400)
        {
            byte[] data = TestData.Bytes(v.Name);
            Check(failures, v.Name + " input", v.Input, "ok|" + TestData.Sha(data));
            Cnab400File file = CnabFile.Parse400(new MemoryStream(data));
            Check(failures, v.Name + " dump", v.Dump, TestData.File400Dump(file));
            Check(failures, v.Name + " write", v.Write, Write400(file));
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void WriteIsByteIdenticalToTheSamples()
    {
        foreach (string name in new[] { "remessa_341.rem", "retorno_341.ret" })
        {
            byte[] data = TestData.Bytes(name);
            var output = new MemoryStream();
            CnabWriter.Default.Write(CnabFile.Parse(new MemoryStream(data)), output);
            Assert.Equal(data, output.ToArray());
        }

        byte[] data400 = TestData.Bytes("remessa_237.rem");
        var output400 = new MemoryStream();
        CnabWriter.Default.Write(CnabFile.Parse400(new MemoryStream(data400)), output400);
        Assert.Equal(data400, output400.ToArray());
    }

    [Fact]
    public void SampleEntriesMatchGo()
    {
        IReadOnlyList<RetornoEntry> entries = Reconciler.EntriesFrom(TestData.Parse("retorno_341.ret"));
        Assert.Equal(GoVectors.SampleEntries, entries.Select(EntryPipe).ToArray());
    }

    [Fact]
    public void BuilderSampleMatchesGoAndTestdata()
    {
        string text = SampleBuilder().BuildText();
        Assert.Equal(GoVectors.BuilderSample, text);
        Assert.Equal(TestData.Text("remessa_341.rem"), text);
    }

    [Fact]
    public void BuilderMatchesGo()
    {
        var failures = new List<string>();
        foreach (var v in GoVectors.Builder)
        {
            string[] c = v.Company.Split('|');
            string[] b = v.Bank.Split('|');
            var company = new Company
            {
                Nome = c[0], Documento = c[1], Convenio = c[2], Agencia = c[3],
                AgenciaDV = c[4], Conta = c[5], ContaDV = c[6], AgenciaContaDV = c[7],
            };
            var bank = new Bank { Codigo = b[0], Nome = b[1], VersaoArquivo = b[2], VersaoLote = b[3] };
            var builder = new RemessaBuilder(company, bank).WithNsa(v.Nsa).WithGeneratedAt(GeneratedAt);
            foreach (string pipe in v.Boletos)
            {
                builder.AddBoleto(ParseBoleto(pipe));
            }

            string actual;
            try
            {
                actual = "ok|" + TestData.Sha(builder.BuildText());
            }
            catch (CnabException e)
            {
                actual = "error|" + e.Message;
            }

            Check(failures, v.Company + " / " + string.Join(" / ", v.Boletos), v.Result, actual);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void RandomSegmentsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string line, string dump) in GoVectors.Segments)
        {
            Segment s = Segment.Decode(line);
            Check(failures, line, dump, TestData.SegmentDump(s));
            Check(failures, line + " encode", line, s.Encode());
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void SegmentMutationsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string line, string decoded, string reencoded) in GoVectors.SegmentMutations)
        {
            string actualDecoded;
            string actualReencoded = string.Empty;
            try
            {
                Segment s = Segment.Decode(line);
                actualDecoded = "ok|" + TestData.SegmentDump(s);
                try
                {
                    string encoded = s.Encode();
                    actualReencoded = encoded == line ? "same" : "line|" + encoded;
                }
                catch (CnabException e)
                {
                    actualReencoded = "error|" + e.Message;
                }
            }
            catch (CnabException e)
            {
                actualDecoded = "error|" + e.Message;
            }

            Check(failures, line + " decode", decoded, actualDecoded);
            Check(failures, line + " reencode", reencoded, actualReencoded);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void ParseMutationsMatchGo()
    {
        var failures = new List<string>();
        foreach (var v in GoVectors.ParseMutations)
        {
            string input = ApplyMutation(TestData.Text(v.File), v.Op, v.Line, v.Pos, v.Text);
            string actual;
            if (v.File.EndsWith(".rem", StringComparison.Ordinal) && v.File.Contains("237", StringComparison.Ordinal))
            {
                try
                {
                    Cnab400File f = CnabFile.Parse400(new StringReader(input));
                    actual = "ok|" + TestData.File400Dump(f) + "\n" + Write400(f);
                }
                catch (CnabException e)
                {
                    actual = "error|" + e.Message;
                }
            }
            else
            {
                try
                {
                    actual = "ok|" + Write(CnabFile.Parse(new StringReader(input)), CnabWriter.Default);
                }
                catch (CnabException e)
                {
                    actual = "error|" + e.Message;
                }

                string totals = Capture(() => CnabFile.Parse(new StringReader(input), new ParseOptions { CheckTotals = true }));
                actual += "\ntotals " + totals;
            }

            Check(failures, $"{v.File} {v.Op} line={v.Line} pos={v.Pos} text={v.Text}", v.Result, actual);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void ReconciliationMatchesGo()
    {
        var failures = new List<string>();
        foreach (var v in GoVectors.Reconcile)
        {
            List<ExpectedBoleto> expected = v.Expected.Select(ParseExpected).ToList();
            List<RetornoEntry> entries = v.Entries.Select(ParseEntry).ToList();
            string summary;
            string results = string.Empty;
            try
            {
                ReconcileReport report = Reconciler.Reconcile(expected, entries, new ReconcileOptions { GraceDays = v.GraceDays });
                summary = report.Summary();
                results = string.Join("\n", report.Results.Select(r =>
                    r.Status.ToSnakeCase() + "|" + r.NossoNumero + "|" + r.Difference.ToString(CultureInfo.InvariantCulture) + "|" + r.Explanation));
                results += string.Create(
                    CultureInfo.InvariantCulture,
                    $"\nattention={(report.NeedsAttention ? "true" : "false")} expected={report.ExpectedTotal} paid={report.PaidTotal} credited={report.CreditedTotal} tariffs={report.TariffTotal}");
            }
            catch (CnabException e)
            {
                summary = "error|" + e.Message;
            }

            Check(failures, v.Name + " summary", v.Summary, summary);
            Check(failures, v.Name + " results", v.Results, results);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void CodecMatchesGo()
    {
        var failures = new List<string>();
        foreach ((string input, string result) in GoVectors.Codec)
        {
            string actual;
            if (input.StartsWith("decode|", StringComparison.Ordinal))
            {
                try
                {
                    actual = "ok|" + TestData.Dump(FixedWidth.Deserialize<Rec>(input.Substring(7)));
                }
                catch (CnabException e)
                {
                    actual = "error|" + e.Message;
                }
            }
            else
            {
                string[] p = input.Split('|');
                var rec = new Rec
                {
                    Tipo = p[0],
                    Codigo = p[1],
                    Qtd = int.Parse(p[2], CultureInfo.InvariantCulture),
                    Nome = p[3],
                    Data = Date(p[4]),
                    Data6 = Date(p[5]),
                    Valor = long.Parse(p[6], CultureInfo.InvariantCulture),
                    Livre = p[7],
                    Small = byte.Parse(p[8], CultureInfo.InvariantCulture),
                };
                try
                {
                    string line = FixedWidth.Serialize(rec, 47);
                    actual = "ok|" + line + "\n" + TestData.Dump(FixedWidth.Deserialize<Rec>(line));
                }
                catch (CnabException e)
                {
                    actual = "error|" + e.Message;
                }
            }

            Check(failures, input, result, actual);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void FoldMatchesGo()
    {
        var failures = new List<string>();
        foreach ((string input, string result) in GoVectors.Fold)
        {
            string actual;
            try
            {
                actual = "ok|" + AsciiFolding.Fold(input);
            }
            catch (CnabException e)
            {
                actual = "error|" + e.Message;
            }

            Check(failures, input, result, actual);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void KeysMatchGo()
    {
        foreach ((string input, string key) in GoVectors.Keys)
        {
            Assert.Equal(key, Reconciler.Key(input));
        }
    }

    internal static RemessaBuilder SampleBuilder()
    {
        var builder = new RemessaBuilder(
                new Company { Nome = "Acme Utilidades Ltda", Documento = "11.222.333/0001-81", Agencia = "01234", Conta = "000000056789", ContaDV = "0" },
                new Bank { Codigo = "341", Nome = "Banco Itaú S.A." })
            .WithNsa(42)
            .WithGeneratedAt(GeneratedAt);
        var joao = new Pagador
        {
            Nome = "João da Silva Sauro", Documento = "529.982.247-25", Endereco = "Rua das Laranjeiras, 1500 apto 302",
            Bairro = "Laranjeiras", CEP = "22240-003", Cidade = "Rio de Janeiro", UF = "RJ",
        };
        var padaria = new Pagador
        {
            Nome = "Padaria Pão Quente Ltda", Documento = "11222333000181", Endereco = "Av. Brasil, 2000",
            Bairro = "Penha", CEP = "21011-000", Cidade = "Rio de Janeiro", UF = "RJ",
        };
        DateOnly em = new(2026, 11, 20);
        return builder
            .AddBoleto(new Boleto { NossoNumero = "00000001", NumeroDocumento = "NF-1001", Vencimento = new DateOnly(2026, 12, 10), Valor = 150000, Pagador = joao, JurosPorDia = 50, Emissao = em })
            .AddBoleto(new Boleto
            {
                NossoNumero = "00000002", NumeroDocumento = "NF-1002", Vencimento = new DateOnly(2026, 12, 15), Valor = 89990, Pagador = padaria,
                JurosPorDia = 30, Multa = 1800, Mensagem = "Não receber após 30 dias do vencimento", Emissao = em, DiasProtesto = 5,
            })
            .AddBoleto(new Boleto { NossoNumero = "00000003", NumeroDocumento = "NF-1003", Vencimento = new DateOnly(2026, 12, 20), Valor = 25000, Pagador = joao, Desconto = 1000, DescontoAte = new DateOnly(2026, 12, 10), Emissao = em })
            .AddBoleto(new Boleto { NossoNumero = "00000004", NumeroDocumento = "NF-1004", Vencimento = new DateOnly(2026, 12, 20), Valor = 42050, Pagador = padaria, Desconto = 2050, Emissao = em })
            .AddBoleto(new Boleto { NossoNumero = "00000005", NumeroDocumento = "NF-1005", Vencimento = new DateOnly(2026, 12, 30), Valor = 10000, Pagador = joao, Emissao = em });
    }

    /// <summary>Applies the same edit as <c>mutate</c> in tools/vecgen.</summary>
    internal static string ApplyMutation(string data, string op, int line, int pos, string text)
    {
        if (data.EndsWith("\r\n", StringComparison.Ordinal))
        {
            data = data.Substring(0, data.Length - 2);
        }

        List<string> lines = data.Split("\r\n").ToList();
        string eol = "\r\n";
        string final = "\r\n";
        switch (op)
        {
            case "set":
                lines[line] = TestData.Patch(lines[line], pos, text);
                break;
            case "del":
                lines.RemoveAt(line);
                break;
            case "dup":
                lines.Insert(line + 1, lines[line]);
                break;
            case "swap":
                (lines[line], lines[line + 1]) = (lines[line + 1], lines[line]);
                break;
            case "trunc":
                lines[line] = lines[line].Substring(0, pos);
                break;
            case "lf":
                eol = "\n";
                final = "\n";
                break;
            case "noeol":
                final = string.Empty;
                break;
            case "ctrlz":
                final = "\r\n" + (char)0x1A;
                break;
            case "blank":
                final = "\r\n\r\n";
                break;
            default:
                throw new InvalidOperationException(op);
        }

        return string.Join(eol, lines) + final;
    }

    private static string Write(Cnab240File file, CnabWriter writer)
    {
        try
        {
            return "ok|" + TestData.Sha(writer.WriteToString(file));
        }
        catch (CnabException e)
        {
            return "write error|" + e.Message;
        }
    }

    private static string Write400(Cnab400File file)
    {
        try
        {
            return "ok|" + TestData.Sha(CnabWriter.Default.WriteToString(file));
        }
        catch (CnabException e)
        {
            return "write error|" + e.Message;
        }
    }

    private static string Capture(Action action)
    {
        try
        {
            action();
            return "ok";
        }
        catch (CnabException e)
        {
            return "error|" + e.Message;
        }
    }

    private static DateOnly? Date(string s) =>
        s.Length == 0 ? null : DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string DateText(DateOnly? d) =>
        d is DateOnly x ? x.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;

    private static long L(string s) => long.Parse(s, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Boleto ParseBoleto(string pipe)
    {
        string[] p = pipe.Split('|');
        return new Boleto
        {
            NossoNumero = p[0],
            NumeroDocumento = p[1],
            Carteira = I(p[2]),
            Especie = (Especie)I(p[3]),
            Aceite = p[4] == "1",
            Emissao = Date(p[5]),
            Vencimento = Date(p[6]),
            Valor = L(p[7]),
            JurosPorDia = L(p[8]),
            Desconto = L(p[9]),
            DescontoAte = Date(p[10]),
            Abatimento = L(p[11]),
            Multa = L(p[12]),
            DiasProtesto = I(p[13]),
            DiasBaixa = I(p[14]),
            UsoEmpresa = p[15],
            Mensagem = p[16],
            Pagador = new Pagador { Nome = p[17], Documento = p[18], Endereco = p[19], Bairro = p[20], CEP = p[21], Cidade = p[22], UF = p[23] },
        };
    }

    private static ExpectedBoleto ParseExpected(string pipe)
    {
        string[] p = pipe.Split('|');
        return new ExpectedBoleto { NossoNumero = p[0], Valor = L(p[1]), Vencimento = Date(p[2]), Referencia = p[3] };
    }

    private static RetornoEntry ParseEntry(string pipe)
    {
        string[] p = pipe.Split('|');
        return new RetornoEntry
        {
            NossoNumero = p[0],
            NumeroDocumento = p[1],
            Movimento = (MovimentoRetorno)I(p[2]),
            Motivos = p[3].Length == 0 ? Array.Empty<string>() : p[3].Split(','),
            Vencimento = Date(p[4]),
            Valor = L(p[5]),
            ValorPago = L(p[6]),
            ValorLiquido = L(p[7]),
            JurosMulta = L(p[8]),
            Desconto = L(p[9]),
            Abatimento = L(p[10]),
            Tarifa = L(p[11]),
            DataOcorrencia = Date(p[12]),
            DataCredito = Date(p[13]),
        };
    }

    private static string EntryPipe(RetornoEntry e) => string.Join("|", new[]
    {
        e.NossoNumero, e.NumeroDocumento, ((int)e.Movimento).ToString(CultureInfo.InvariantCulture), string.Join(",", e.Motivos), DateText(e.Vencimento),
        e.Valor.ToString(CultureInfo.InvariantCulture), e.ValorPago.ToString(CultureInfo.InvariantCulture), e.ValorLiquido.ToString(CultureInfo.InvariantCulture),
        e.JurosMulta.ToString(CultureInfo.InvariantCulture), e.Desconto.ToString(CultureInfo.InvariantCulture), e.Abatimento.ToString(CultureInfo.InvariantCulture),
        e.Tarifa.ToString(CultureInfo.InvariantCulture), DateText(e.DataOcorrencia), DateText(e.DataCredito),
    });

    private static void Check(List<string> failures, string label, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            failures.Add(label + "\n    go:     " + expected + "\n    dotnet: " + actual);
        }
    }
}
