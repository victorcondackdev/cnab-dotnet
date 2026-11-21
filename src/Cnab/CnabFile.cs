using System.Text;
using Cnab.Cnab240;
using Cnab.Cnab400;
using Cnab.Internal;

namespace Cnab;

/// <summary>A CNAB layout family, identified by record length.</summary>
public enum FileLayout
{
    /// <summary>Neither 240 nor 400 positions.</summary>
    Unknown = 0,

    /// <summary>CNAB 240.</summary>
    Cnab240 = 240,

    /// <summary>CNAB 400.</summary>
    Cnab400 = 400,
}

/// <summary>Options for <see cref="CnabFile.Parse(TextReader, ParseOptions)"/>.</summary>
public sealed record ParseOptions
{
    /// <summary>The default options.</summary>
    public static ParseOptions Default { get; } = new();

    /// <summary>
    /// Also verifies the batch trailer title quantities and amounts (positions
    /// 24-115) against the batch's P or T segments when the trailer reports
    /// them (any non-zero total). Off by default because several banks report
    /// portfolio-wide totals there in retorno files.
    /// </summary>
    public bool CheckTotals { get; init; }
}

/// <summary>
/// Reads whole CNAB files: splits lines, detects the layout, groups CNAB 240
/// batches and segments, and validates trailer counts and sequence numbers.
/// </summary>
/// <remarks>
/// Lines may end with LF or CRLF, the final terminator is optional and a
/// trailing DOS end-of-file marker (0x1A) is ignored. Streams are decoded as
/// Latin-1 (ISO-8859-1), one character per byte, so any input round-trips
/// byte for byte through <see cref="CnabWriter"/>.
/// </remarks>
public static class CnabFile
{
    private const char EofMarker = (char)0x1A;
    private const int MaxLineLength = 1 << 20;

    /// <summary>The encoding used for streams: Latin-1, one character per byte.</summary>
    public static Encoding TextEncoding { get; } = Encoding.Latin1;

    /// <summary>Formats a layout as <c>CNAB 240</c>, <c>CNAB 400</c> or <c>unknown</c>.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The display name.</returns>
    public static string Describe(this FileLayout layout) => layout switch
    {
        FileLayout.Cnab240 => "CNAB 240",
        FileLayout.Cnab400 => "CNAB 400",
        _ => "unknown",
    };

    /// <summary>Reports the layout of <paramref name="data"/> from the length of its first line.</summary>
    /// <param name="data">The file content.</param>
    /// <returns>The layout.</returns>
    public static FileLayout Detect(ReadOnlySpan<byte> data)
    {
        int nl = data.IndexOf((byte)'\n');
        ReadOnlySpan<byte> line = nl >= 0 ? data.Slice(0, nl) : data;
        if (line.Length > 0 && line[^1] == (byte)'\r')
        {
            line = line.Slice(0, line.Length - 1);
        }

        return FromLength(line.Length);
    }

    /// <summary>Reports the layout of <paramref name="text"/> from the length of its first line.</summary>
    /// <param name="text">The file content.</param>
    /// <returns>The layout.</returns>
    public static FileLayout Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int nl = text.IndexOf('\n', StringComparison.Ordinal);
        int length = nl >= 0 ? nl : text.Length;
        if (length > 0 && text[length - 1] == '\r')
        {
            length--;
        }

        return FromLength(length);
    }

    private static FileLayout FromLength(int length) => length switch
    {
        240 => FileLayout.Cnab240,
        400 => FileLayout.Cnab400,
        _ => FileLayout.Unknown,
    };

    /// <summary>Reads and validates a CNAB 240 file from a stream (Latin-1).</summary>
    /// <param name="stream">The file content. It is not closed.</param>
    /// <param name="options">Parsing options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="CnabException">The file is empty, malformed or inconsistent; see <see cref="Parse(TextReader, ParseOptions)"/>.</exception>
    public static Cnab240File Parse(Stream stream, ParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, TextEncoding, detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024, leaveOpen: true);
        return Parse(reader, options);
    }

    /// <summary>
    /// Reads a CNAB 240 file and validates its structure.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>every line is 240 characters and has the header's bank code;</item>
    /// <item>the file starts with a header (lote 0000) and ends with a trailer (lote 9999), and batches are numbered 1, 2, ... in order;</item>
    /// <item>every record of a batch carries the batch number, detail records are numbered 1, 2, ... and the batch trailer counts its records;</item>
    /// <item>the file trailer counts batches and lines.</item>
    /// </list>
    /// A 400-position input fails with <see cref="CnabError.Format"/>; use <see cref="Parse400(TextReader)"/>.
    /// </remarks>
    /// <param name="reader">The file content.</param>
    /// <param name="options">Parsing options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="CnabException"><see cref="CnabError.Empty"/> for an input with no records.</exception>
    /// <exception cref="CnabLineException">A structural problem, located by line.</exception>
    /// <exception cref="CnabFieldException">A field holds an invalid value; <see cref="CnabFieldException.Line"/> is set.</exception>
    public static Cnab240File Parse(TextReader reader, ParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= ParseOptions.Default;
        List<string> lines = ReadLines(reader);
        if (lines.Count == 0)
        {
            throw new CnabException(CnabError.Empty);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            string l = lines[i];
            if (l.Length != Layout240.LineLength)
            {
                if (i == 0 && l.Length == Layout400.LineLength)
                {
                    throw new CnabLineException(1, CnabError.Format, "this is a CNAB 400 file; use Parse400");
                }

                throw new CnabLineException(i + 1, CnabError.Format, string.Create(GoText.Inv, $"line has {l.Length} bytes, want 240"));
            }
        }

        var file = new Cnab240File();
        if (lines[0][7] != RecordType.HeaderArquivo)
        {
            throw new CnabLineException(1, CnabError.Structure, "first record must be a file header (type 0), have type " + GoText.QuoteRune(lines[0][7]));
        }

        file.Header = Decode<FileHeader>(lines[0], 1);
        if (file.Header.Lote != 0)
        {
            throw new CnabLineException(1, CnabError.Sequence, string.Create(GoText.Inv, $"file header lote is {file.Header.Lote:D4}, want 0000"));
        }

        string bank = lines[0].Substring(0, 3);
        Batch? batch = null;
        int detailSeq = 0;
        int last = lines.Count - 1;
        for (int i = 1; i < lines.Count; i++)
        {
            int n = i + 1;
            string l = lines[i];
            if (!l.AsSpan(0, 3).SequenceEqual(bank.AsSpan()))
            {
                throw new CnabLineException(n, CnabError.Bank, "bank " + l.Substring(0, 3) + ", file header has " + bank);
            }

            char type = l[7];
            switch (type)
            {
                case RecordType.HeaderLote:
                {
                    if (batch is not null)
                    {
                        throw new CnabLineException(n, CnabError.Structure, string.Create(GoText.Inv, $"batch header inside batch {file.Batches.Count} (missing trailer)"));
                    }

                    batch = new Batch { Header = Decode<BatchHeader>(l, n) };
                    file.Batches.Add(batch);
                    if (batch.Header.Lote != file.Batches.Count)
                    {
                        throw new CnabLineException(n, CnabError.Sequence, string.Create(GoText.Inv, $"batch number {batch.Header.Lote}, want {file.Batches.Count}"));
                    }

                    detailSeq = 0;
                    break;
                }

                case RecordType.IniciaisLote:
                case RecordType.Detalhe:
                case RecordType.FinaisLote:
                {
                    if (batch is null)
                    {
                        throw new CnabLineException(n, CnabError.Structure, "record type " + type + " outside a batch");
                    }

                    CheckLote(l, batch.Header.Lote, n);
                    if (type == RecordType.Detalhe)
                    {
                        detailSeq++;
                        string got = l.Substring(8, 5);
                        if (!DigitsEqual(got, detailSeq))
                        {
                            throw new CnabLineException(n, CnabError.Sequence, string.Create(GoText.Inv, $"record sequence {GoText.QuoteBytes(got)}, want {detailSeq:D5}"));
                        }
                    }

                    try
                    {
                        batch.Segments.Add(Segment.Decode(l));
                    }
                    catch (CnabFieldException e)
                    {
                        throw e.WithLine(n);
                    }

                    break;
                }

                case RecordType.TrailerLote:
                {
                    if (batch is null)
                    {
                        throw new CnabLineException(n, CnabError.Structure, "batch trailer outside a batch");
                    }

                    batch.Trailer = Decode<BatchTrailer>(l, n);
                    if (batch.Trailer.Lote != batch.Header.Lote)
                    {
                        throw new CnabLineException(n, CnabError.Sequence, string.Create(GoText.Inv, $"batch trailer lote {batch.Trailer.Lote:D4}, batch is {batch.Header.Lote:D4}"));
                    }

                    int want = batch.Segments.Count + 2;
                    if (batch.Trailer.QuantidadeRegistros != want)
                    {
                        throw new CnabLineException(n, CnabError.Count, string.Create(GoText.Inv, $"batch {batch.Header.Lote} trailer reports {batch.Trailer.QuantidadeRegistros} records, counted {want}"));
                    }

                    if (options.CheckTotals)
                    {
                        CheckTotals(batch, n);
                    }

                    batch = null;
                    break;
                }

                case RecordType.TrailerArquivo:
                {
                    if (i != last)
                    {
                        throw new CnabLineException(n, CnabError.Structure, "file trailer is not the last record");
                    }

                    if (batch is not null)
                    {
                        throw new CnabLineException(n, CnabError.Structure, string.Create(GoText.Inv, $"batch {batch.Header.Lote} has no trailer"));
                    }

                    file.Trailer = Decode<FileTrailer>(l, n);
                    if (file.Trailer.Lote != 9999)
                    {
                        throw new CnabLineException(n, CnabError.Sequence, string.Create(GoText.Inv, $"file trailer lote {file.Trailer.Lote:D4}, want 9999"));
                    }

                    if (file.Trailer.QuantidadeLotes != file.Batches.Count)
                    {
                        throw new CnabLineException(n, CnabError.Count, string.Create(GoText.Inv, $"file trailer reports {file.Trailer.QuantidadeLotes} batches, counted {file.Batches.Count}"));
                    }

                    if (file.Trailer.QuantidadeRegistros != lines.Count)
                    {
                        throw new CnabLineException(n, CnabError.Count, string.Create(GoText.Inv, $"file trailer reports {file.Trailer.QuantidadeRegistros} records, counted {lines.Count}"));
                    }

                    return file;
                }

                default:
                    throw new CnabLineException(n, CnabError.Structure, "unexpected record type " + GoText.QuoteRune(type));
            }
        }

        throw new CnabLineException(lines.Count, CnabError.Structure, "missing file trailer (type 9)");
    }

    /// <summary>Reads and validates a CNAB 400 file from a stream (Latin-1).</summary>
    /// <param name="stream">The file content. It is not closed.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="CnabException">The file is empty, malformed or inconsistent.</exception>
    public static Cnab400File Parse400(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, TextEncoding, detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024, leaveOpen: true);
        return Parse400(reader);
    }

    /// <summary>
    /// Reads a CNAB 400 file: a header (type 0), detail records and a trailer
    /// (type 9), with sequence numbers (395-400) 1, 2, ... in order. Details
    /// are kept generic; decode them with a bank-specific record.
    /// </summary>
    /// <param name="reader">The file content.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="CnabException"><see cref="CnabError.Empty"/> for an input with no records.</exception>
    /// <exception cref="CnabLineException">A structural problem, located by line.</exception>
    /// <exception cref="CnabFieldException">A field holds an invalid value; <see cref="CnabFieldException.Line"/> is set.</exception>
    public static Cnab400File Parse400(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        List<string> lines = ReadLines(reader);
        if (lines.Count == 0)
        {
            throw new CnabException(CnabError.Empty);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            string l = lines[i];
            if (l.Length != Layout400.LineLength)
            {
                if (i == 0 && l.Length == Layout240.LineLength)
                {
                    throw new CnabLineException(1, CnabError.Format, "this is a CNAB 240 file; use Parse");
                }

                throw new CnabLineException(i + 1, CnabError.Format, string.Create(GoText.Inv, $"line has {l.Length} bytes, want 400"));
            }

            string got = l.Substring(394);
            if (!DigitsEqual(got, i + 1))
            {
                throw new CnabLineException(i + 1, CnabError.Sequence, string.Create(GoText.Inv, $"sequence {GoText.QuoteBytes(got)}, want {i + 1:D6}"));
            }
        }

        if (lines.Count < 2)
        {
            throw new CnabLineException(1, CnabError.Structure, "a CNAB 400 file needs at least a header and a trailer");
        }

        var file = new Cnab400File { Details = new List<Detail>(lines.Count - 2) };
        if (lines[0][0] != '0')
        {
            throw new CnabLineException(1, CnabError.Structure, "first record must be a header (type 0)");
        }

        file.Header = Decode<Cnab400.Header>(lines[0], 1);
        int last = lines.Count - 1;
        if (lines[last][0] != '9')
        {
            throw new CnabLineException(last + 1, CnabError.Structure, "last record must be a trailer (type 9)");
        }

        file.Trailer = Decode<Trailer>(lines[last], last + 1);
        for (int i = 1; i < last; i++)
        {
            char c = lines[i][0];
            if (c == '0' || c == '9')
            {
                throw new CnabLineException(i + 1, CnabError.Structure, "record type " + c + " in the middle of the file");
            }

            file.Details.Add(Decode<Detail>(lines[i], i + 1));
        }

        return file;
    }

    private static T Decode<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(FixedWidth.RecordMembers)] T>(string line, int lineNumber)
        where T : class, new()
    {
        try
        {
            return FixedWidth.Deserialize<T>(line);
        }
        catch (CnabFieldException e)
        {
            throw e.WithLine(lineNumber);
        }
    }

    private static void CheckLote(string line, int lote, int n)
    {
        string got = line.Substring(3, 4);
        if (!DigitsEqual(got, lote))
        {
            throw new CnabLineException(n, CnabError.Sequence, string.Create(GoText.Inv, $"record lote {GoText.QuoteBytes(got)}, batch is {lote:D4}"));
        }
    }

    private static void CheckTotals(Batch batch, int n)
    {
        BatchTrailer t = batch.Trailer;
        long quantity = (long)t.QuantidadeSimples + t.QuantidadeVinculada + t.QuantidadeCaucionada + t.QuantidadeDescontada;
        long sum = t.ValorSimples + t.ValorVinculada + t.ValorCaucionada + t.ValorDescontada;
        if (quantity == 0 && sum == 0)
        {
            return;
        }

        int titles = 0;
        long total = 0;
        foreach (Segment s in batch.Segments)
        {
            switch (s)
            {
                case SegmentP p:
                    titles++;
                    total += p.Valor;
                    break;
                case SegmentT tt:
                    titles++;
                    total += tt.Valor;
                    break;
            }
        }

        if (quantity != titles)
        {
            throw new CnabLineException(n, CnabError.Count, string.Create(GoText.Inv, $"batch {batch.Header.Lote} trailer reports {quantity} titles, counted {titles}"));
        }

        if (sum != total)
        {
            throw new CnabLineException(n, CnabError.Count, string.Create(GoText.Inv, $"batch {batch.Header.Lote} trailer reports total {sum} cents, summed {total}"));
        }
    }

    /// <summary>Reports whether <paramref name="s"/> is <paramref name="n"/> written with exactly <c>s.Length</c> digits.</summary>
    internal static bool DigitsEqual(string s, int n)
    {
        for (int i = s.Length - 1; i >= 0; i--)
        {
            if (s[i] != (char)('0' + (n % 10)))
            {
                return false;
            }

            n /= 10;
        }

        return n == 0;
    }

    /// <summary>
    /// Splits the input like Go's <c>bufio.ScanLines</c>: on LF, dropping one
    /// trailing CR per line, with an optional final terminator; then drops a
    /// trailing DOS end-of-file marker.
    /// </summary>
    internal static List<string> ReadLines(TextReader reader)
    {
        string all = reader.ReadToEnd();
        var lines = new List<string>(all.Length / 241 + 1);
        int start = 0;
        while (start < all.Length)
        {
            int nl = all.IndexOf('\n', start);
            int end = nl < 0 ? all.Length : nl;
            if (end - start >= MaxLineLength)
            {
                throw new CnabLineException(lines.Count + 1, CnabError.Format, "line longer than 1 MiB");
            }

            int length = end - start;
            if (length > 0 && all[end - 1] == '\r')
            {
                length--;
            }

            lines.Add(all.Substring(start, length));
            if (nl < 0)
            {
                break;
            }

            start = nl + 1;
        }

        while (lines.Count > 0)
        {
            string lastLine = lines[^1];
            string trimmed = lastLine.TrimEnd(EofMarker);
            if (trimmed.Length != 0)
            {
                lines[^1] = trimmed;
                break;
            }

            if (trimmed.Length == lastLine.Length)
            {
                break; // an empty line that was not an end-of-file marker
            }

            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}
