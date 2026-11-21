using System.Text;
using Cnab.Cnab240;
using Cnab.Cnab400;
using Cnab.Cnab400.Bradesco;
using static Cnab.Tests.TestData;

namespace Cnab.Tests;

public class FileTests
{
    private const string Remessa = "remessa_341.rem";
    private const string Retorno = "retorno_341.ret";
    private const string Remessa400 = "remessa_237.rem";

    [Theory]
    [InlineData(Remessa)]
    [InlineData(Retorno)]
    public void WriteOfParseReproducesTheSample(string name)
    {
        string data = Text(name);
        Cnab240File file = CnabFile.Parse(new StringReader(data));
        Assert.Equal(data, CnabWriter.Default.WriteToString(file));

        // LF without a final newline parses identically and round-trips with
        // the matching writer.
        string lf = data.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        Cnab240File lfFile = CnabFile.Parse(new StringReader(lf));
        Assert.Equal(lf, new CnabWriter { LineEnding = "\n", OmitFinalNewline = true }.WriteToString(lfFile));
    }

    [Fact]
    public void StreamsAreReadAndWrittenAsLatin1()
    {
        byte[] data = Bytes(Retorno);
        Cnab240File file = CnabFile.Parse(new MemoryStream(data));
        var output = new MemoryStream();
        CnabWriter.Default.Write(file, output);
        Assert.Equal(data, output.ToArray());

        // Characters outside Latin-1 are rejected instead of being replaced.
        file.Batches[0].Segments[0] = new RawSegment(Patch(Lines(Retorno)[2], 200, "€"));
        Assert.ThrowsAny<ArgumentException>(() => CnabWriter.Default.Write(file, new MemoryStream()));
    }

    /// <summary>Checks the sample remessa at raw offsets taken from the FEBRABAN manual, independently of the attributes.</summary>
    [Theory]
    [InlineData(0, 1, 3, "341")]
    [InlineData(0, 4, 7, "0000")]
    [InlineData(0, 8, 8, "0")]
    [InlineData(0, 18, 18, "2")]
    [InlineData(0, 19, 32, "11222333000181")]
    [InlineData(0, 143, 143, "1")]
    [InlineData(0, 144, 151, "20112026")]
    [InlineData(0, 152, 157, "093015")]
    [InlineData(0, 158, 163, "000042")]
    [InlineData(1, 8, 8, "1")]
    [InlineData(1, 9, 9, "R")]
    [InlineData(1, 10, 11, "01")]
    [InlineData(1, 19, 33, "011222333000181")]
    [InlineData(1, 184, 191, "00000042")]
    [InlineData(2, 8, 8, "3")]
    [InlineData(2, 9, 13, "00001")]
    [InlineData(2, 14, 14, "P")]
    [InlineData(2, 16, 17, "01")]
    [InlineData(2, 38, 45, "00000001")]
    [InlineData(2, 63, 69, "NF-1001")]
    [InlineData(2, 78, 85, "10122026")]
    [InlineData(2, 86, 100, "000000000150000")]
    [InlineData(2, 107, 108, "02")]
    [InlineData(2, 109, 109, "N")]
    [InlineData(2, 118, 118, "1")]
    [InlineData(2, 119, 126, "11122026")]
    [InlineData(2, 127, 141, "000000000000050")]
    [InlineData(2, 228, 229, "09")]
    [InlineData(3, 14, 14, "Q")]
    [InlineData(3, 18, 18, "1")]
    [InlineData(3, 19, 33, "000052998224725")]
    [InlineData(3, 34, 52, "JOAO DA SILVA SAURO")]
    [InlineData(3, 129, 136, "22240003")]
    [InlineData(3, 137, 150, "RIO DE JANEIRO")]
    [InlineData(3, 152, 153, "RJ")]
    [InlineData(6, 14, 14, "R")]
    [InlineData(6, 66, 66, "1")]
    [InlineData(6, 67, 74, "16122026")]
    [InlineData(6, 75, 89, "000000000001800")]
    [InlineData(13, 8, 8, "5")]
    [InlineData(13, 18, 23, "000013")]
    [InlineData(14, 4, 7, "9999")]
    [InlineData(14, 18, 23, "000001")]
    [InlineData(14, 24, 29, "000015")]
    public void RemessaPositions(int line, int start, int end, string want)
    {
        Assert.Equal(want, Lines(Remessa)[line].Substring(start - 1, end - start + 1));
    }

    [Fact]
    public void ParseRetorno()
    {
        Cnab240File f = CnabFile.Parse(new StringReader(Text(Retorno)), new ParseOptions { CheckTotals = true });
        Assert.Equal(CobrancaCodes.CodigoRetorno, f.Header.CodigoRemessaRetorno);
        Assert.Equal("ACME UTILIDADES LTDA", f.Header.NomeEmpresa);
        IReadOnlyList<Ocorrencia> occurrences = f.Ocorrencias();
        Assert.Equal(9, occurrences.Count);
        Ocorrencia o = occurrences[4];
        Assert.Equal("00000002", o.T.NossoNumero);
        Assert.Equal(MovimentoRetorno.Liquidacao, o.T.Movimento);
        Assert.NotNull(o.U);
        Assert.Equal(91880, o.U.ValorPago);
        Assert.Equal(1890, o.U.JurosMulta);
        Assert.Equal(D(2026, 12, 17), o.U.DataOcorrencia);
        Assert.Equal(350, o.T.Tarifa);
        Assert.Equal(new[] { "08", "46" }, occurrences[2].T.MotivoCodes());

        var liquidacoes = occurrences.Skip(3).Take(3)
            .Select(x => x.T.NossoNumero + " " + x.T.Movimento.Label() + " pago=" + x.U!.ValorPago.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal(
            new[] { "00000001 06 Liquidação pago=150000", "00000002 06 Liquidação pago=91880", "00000004 06 Liquidação pago=40000" },
            liquidacoes);
    }

    [Fact]
    public void ParseRemessaTitulos()
    {
        IReadOnlyList<Titulo> titles = Parse(Remessa).Titulos();
        Assert.Equal(5, titles.Count);
        Assert.NotNull(titles[1].R);
        Assert.Null(titles[0].R);
        Assert.NotNull(titles[1].Q);
        Assert.Equal("PADARIA PAO QUENTE LTDA", titles[1].Q!.Nome);
        Assert.Equal("NAO RECEBER APOS 30 DIAS DO VENCIMENTO", titles[1].R!.Mensagem3);
    }

    private static List<string> Mutate(string name)
    {
        List<string> l = Lines(Remessa);
        switch (name)
        {
            case "short line":
                l[3] = l[3].Substring(0, 239);
                break;
            case "no header":
                l.RemoveAt(0);
                break;
            case "no trailer":
                l.RemoveAt(l.Count - 1);
                break;
            case "trailer not last":
                l.Add(l[^1]);
                break;
            case "bank":
                l[2] = Patch(l[2], 1, "001");
                break;
            case "header lote":
                l[0] = Patch(l[0], 4, "0001");
                break;
            case "batch number":
                l[1] = Patch(l[1], 4, "0002");
                break;
            case "segment lote":
                l[4] = Patch(l[4], 4, "0002");
                break;
            case "segment seq":
                l[4] = Patch(l[4], 9, "00009");
                break;
            case "batch count":
                l[13] = Patch(l[13], 18, "000099");
                break;
            case "batch trailer lote":
                l[13] = Patch(l[13], 4, "0003");
                break;
            case "file lotes":
                l[14] = Patch(l[14], 18, "000002");
                break;
            case "file records":
                l[14] = Patch(l[14], 24, "000016");
                break;
            case "file trailer lote":
                l[14] = Patch(l[14], 4, "9998");
                break;
            case "segment outside batch":
                l.Insert(1, l[2]);
                break;
            case "trailer outside batch":
                l.Insert(1, l[13]);
                break;
            case "nested batch":
                l.Insert(3, l[1]);
                break;
            case "unclosed batch":
                l.RemoveAt(13);
                break;
            case "record type":
                l[2] = Patch(l[2], 8, "7");
                break;
            case "field":
                l[2] = Patch(l[2], 86, "00000000000A000");
                break;
            case "header field":
                l[0] = Patch(l[0], 144, "32132026");
                break;
            case "batch header field":
                l[1] = Patch(l[1], 10, "X1");
                break;
            case "batch trailer field":
                l[13] = Patch(l[13], 24, "00000X");
                break;
            case "file trailer field":
                l[14] = Patch(l[14], 30, "00000X");
                break;
            default:
                throw new InvalidOperationException(name);
        }

        return l;
    }

    [Theory]
    [InlineData("short line", CnabError.Format, 4)]
    [InlineData("no header", CnabError.Structure, 1)]
    [InlineData("no trailer", CnabError.Structure, 14)]
    [InlineData("trailer not last", CnabError.Structure, 15)]
    [InlineData("bank", CnabError.Bank, 3)]
    [InlineData("header lote", CnabError.Sequence, 1)]
    [InlineData("batch number", CnabError.Sequence, 2)]
    [InlineData("segment lote", CnabError.Sequence, 5)]
    [InlineData("segment seq", CnabError.Sequence, 5)]
    [InlineData("batch count", CnabError.Count, 14)]
    [InlineData("batch trailer lote", CnabError.Sequence, 14)]
    [InlineData("file lotes", CnabError.Count, 15)]
    [InlineData("file records", CnabError.Count, 15)]
    [InlineData("file trailer lote", CnabError.Sequence, 15)]
    [InlineData("segment outside batch", CnabError.Structure, 2)]
    [InlineData("trailer outside batch", CnabError.Structure, 2)]
    [InlineData("nested batch", CnabError.Structure, 4)]
    [InlineData("unclosed batch", CnabError.Structure, 14)]
    [InlineData("record type", CnabError.Structure, 3)]
    [InlineData("field", CnabError.Digits, 3)]
    [InlineData("header field", CnabError.Date, 1)]
    [InlineData("batch header field", CnabError.Digits, 2)]
    [InlineData("batch trailer field", CnabError.Digits, 14)]
    [InlineData("file trailer field", CnabError.Digits, 15)]
    public void ParseErrorsCarryKindAndLine(string mutation, CnabError error, int line)
    {
        CnabException e = Assert.ThrowsAny<CnabException>(() => CnabFile.Parse(Reader(Mutate(mutation))));
        Assert.Equal(error, e.Error);
        int actualLine = e switch
        {
            CnabLineException le => le.Line,
            CnabFieldException fe => fe.Line,
            _ => -1,
        };
        Assert.Equal(line, actualLine);
    }

    [Fact]
    public void ParseErrorMessagesMatchGo()
    {
        Assert.Equal(
            "cnab: line 14: cnab: trailer count mismatch: batch 1 trailer reports 99 records, counted 13",
            Assert.Throws<CnabLineException>(() => CnabFile.Parse(Reader(Mutate("batch count")))).Message);
        Assert.Equal(
            "cnab: line 5: cnab: sequence number mismatch: record sequence \"00009\", want 00003",
            Assert.Throws<CnabLineException>(() => CnabFile.Parse(Reader(Mutate("segment seq")))).Message);
        Assert.Equal(
            "cnab: line 3: field Valor (86-100) value \"00000000000A000\": must contain only digits",
            Assert.Throws<CnabFieldException>(() => CnabFile.Parse(Reader(Mutate("field")))).Message);
        Assert.Equal(
            "cnab: line 3: cnab: invalid file structure: unexpected record type '7'",
            Assert.Throws<CnabLineException>(() => CnabFile.Parse(Reader(Mutate("record type")))).Message);
    }

    [Fact]
    public void OtherParseErrors()
    {
        Assert.Equal(CnabError.Empty, Assert.Throws<CnabException>(() => CnabFile.Parse(new StringReader(string.Empty))).Error);
        CnabLineException e = Assert.Throws<CnabLineException>(() => CnabFile.Parse(new StringReader(Text(Remessa400))));
        Assert.Equal(CnabError.Format, e.Error);
        Assert.Contains("Parse400", e.Message, StringComparison.Ordinal);
        Assert.Throws<IOException>(() => CnabFile.Parse(new FailingReader()));
        Assert.Throws<ArgumentNullException>(() => CnabFile.Parse((TextReader)null!));
    }

    [Fact]
    public void CheckTotals()
    {
        List<string> l = Lines(Remessa);

        // 5 titles totalling 150000+89990+25000+42050+10000 = 317040.
        string good = Patch(l[13], 24, "000005" + "00000000000317040");
        l[13] = good;
        var options = new ParseOptions { CheckTotals = true };
        CnabFile.Parse(Reader(l), options);

        l[13] = Patch(good, 24, "000004");
        Assert.Equal(CnabError.Count, Assert.Throws<CnabLineException>(() => CnabFile.Parse(Reader(l), options)).Error);

        l[13] = Patch(good, 30, "00000000000317041");
        Assert.Equal(CnabError.Count, Assert.Throws<CnabLineException>(() => CnabFile.Parse(Reader(l), options)).Error);

        // Totals are not checked by default.
        CnabFile.Parse(Reader(l));
    }

    [Theory]
    [InlineData("ctrl-z")]
    [InlineData("no final")]
    [InlineData("lf")]
    [InlineData("mixed eol")]
    public void ToleratesLineEndingVariants(string variant)
    {
        string data = Text(Remessa);
        string input = variant switch
        {
            "ctrl-z" => data + (char)0x1A,
            "no final" => data.Substring(0, data.Length - 2),
            "lf" => data.Replace("\r\n", "\n", StringComparison.Ordinal),
            _ => ReplaceFirst(data, 3),
        };
        Assert.Equal(15, CnabFile.Parse(new StringReader(input)).Trailer.QuantidadeRegistros);
    }

    private static string ReplaceFirst(string data, int count)
    {
        var b = new StringBuilder(data);
        int at = 0;
        for (int i = 0; i < count; i++)
        {
            at = b.ToString().IndexOf("\r\n", at, StringComparison.Ordinal);
            b.Remove(at, 1);
        }

        return b.ToString();
    }

    [Fact]
    public void RawSegmentsRoundTrip()
    {
        List<string> l = Lines(Remessa);

        // Turn the R segment into an unknown segment code "Y" and add a type 4
        // record: both must be preserved verbatim.
        l[6] = Patch(l[6], 14, "Y");
        string record4 = Patch(new string(' ', 240), 1, "341000142");
        l.Insert(13, record4);
        l[14] = Patch(l[14], 18, "000014");
        l[15] = Patch(l[15], 24, "000016");
        string input = Join(l);
        Cnab240File f = CnabFile.Parse(new StringReader(input));
        List<Segment> segments = f.Batches[0].Segments;
        Assert.Equal('Y', Assert.IsType<RawSegment>(segments[4]).Code);
        Assert.Equal((char)0, Assert.IsType<RawSegment>(segments[11]).Code);
        Assert.Equal(input, CnabWriter.Default.WriteToString(f));
    }

    [Fact]
    public void WriteRegeneratesControlFields()
    {
        Cnab240File f = Parse(Remessa);
        Batch original = f.Batches[0];
        var copy = new Batch { Header = original.Header with { Lote = 77 }, Segments = new List<Segment>(original.Segments), Trailer = original.Trailer };
        f.Batches.Add(copy);
        f.Trailer.QuantidadeLotes = 0;
        original.Trailer.QuantidadeRegistros = 1;
        ((SegmentP)original.Segments[0]).Sequencial = 40;

        Cnab240File g = CnabFile.Parse(new StringReader(CnabWriter.Default.WriteToString(f)));
        Assert.Equal(2, g.Batches.Count);
        Assert.Equal(28, g.Trailer.QuantidadeRegistros);
        Assert.Equal(2, g.Batches[1].Header.Lote);
        Assert.Equal(40, ((SegmentP)f.Batches[0].Segments[0]).Sequencial);
    }

    public static readonly TheoryData<string> WriteErrorCases = new()
    {
        "header", "batch header", "segment", "raw", "batch trailer", "file trailer", "too many", "too many segments", "too many records",
    };

    [Theory]
    [MemberData(nameof(WriteErrorCases))]
    public void WriteErrors(string name)
    {
        Cnab240File f = Parse(Remessa);
        Batch b = f.Batches[0];
        switch (name)
        {
            case "header":
                f.Header.Agencia = "x";
                break;
            case "batch header":
                b.Header.Agencia = "x";
                break;
            case "segment":
                b.Segments[0] = new SegmentP { Agencia = "x" };
                break;
            case "raw":
                b.Segments[0] = new RawSegment("x");
                break;
            case "batch trailer":
                b.Trailer.NumeroAvisoLancamento = "x";
                break;
            case "file trailer":
                f.Trailer.CNAB2 = new string('x', 300);
                break;
            case "too many":
                f.Batches = Enumerable.Range(0, 9999).Select(_ => new Batch()).ToList();
                break;
            case "too many segments":
                b.Segments = Enumerable.Repeat<Segment>(new RawSegment(string.Empty), 100000).ToList();
                break;
            default:
                var raw = new RawSegment(new string('4', 240));
                List<Segment> segments = Enumerable.Repeat<Segment>(raw, 99999).ToList();
                f.Batches = Enumerable.Range(0, 11).Select(_ => new Batch { Segments = segments }).ToList();
                break;
        }

        Assert.ThrowsAny<CnabException>(() => CnabWriter.Default.WriteToString(f));
    }

    [Fact]
    public void WriteErrorMessagesNameTheRecord()
    {
        Cnab240File f = Parse(Remessa);
        f.Batches[0].Header.Agencia = "x";
        CnabFieldException e = Assert.Throws<CnabFieldException>(() => CnabWriter.Default.WriteToString(f));
        Assert.Equal("batch 1 header", e.Context);
        Assert.Equal("batch 1 header: cnab: field Agencia (54-58) value \"x\": must contain only digits", e.Message);

        f = Parse(Remessa);
        f.Batches[0].Segments[2] = new RawSegment("x");
        Assert.Equal(
            "batch 1 record 3: cnab240: raw segment must be 240 bytes, have 1",
            Assert.Throws<CnabException>(() => CnabWriter.Default.WriteToString(f)).Message);

        f = Parse(Remessa);
        f.Batches = Enumerable.Range(0, 9999).Select(_ => new Batch()).ToList();
        Assert.Equal(
            "cnab: trailer count mismatch: 9999 batches, at most 9998 fit",
            Assert.Throws<CnabException>(() => CnabWriter.Default.WriteToString(f)).Message);
    }

    [Fact]
    public void Parse400()
    {
        string data = Text(Remessa400);
        Cnab400File f = CnabFile.Parse400(new StringReader(data));
        Assert.Equal("237", f.Header.Banco);
        Assert.Equal("COBRANCA", f.Header.LiteralServico);
        Assert.Equal(2, f.Details.Count);
        Assert.Equal(D(2026, 11, 20), f.Header.DataGravacao);

        DetalheRemessa det = f.Details[1].Decode<DetalheRemessa>();
        Assert.Equal("00000000102", det.NossoNumero);
        Assert.Equal(24000, det.Valor);
        Assert.Equal(3, det.Sequencial);
        Assert.Equal(data, CnabWriter.Default.WriteToString(f));
    }

    public static readonly TheoryData<string> Bad400Cases = new()
    {
        "seq", "short", "one line", "no header", "no trailer", "middle 9", "header field", "trailer field", "240",
    };

    [Theory]
    [MemberData(nameof(Bad400Cases))]
    public void Parse400Errors(string name)
    {
        List<string> l = Lines(Remessa400);
        List<string> input = name switch
        {
            "seq" => new() { l[0], l[2], l[1], l[3] },
            "short" => new() { l[0], l[1].Substring(0, 399) },
            "one line" => new() { l[0] },
            "no header" => new() { l[1], Patch(l[1], 395, "000002"), Patch(l[3], 395, "000003") },
            "no trailer" => new() { l[0], Patch(l[1], 395, "000002") },
            "middle 9" => new() { l[0], Patch(l[3], 395, "000002"), Patch(l[3], 395, "000003") },
            "header field" => new() { Patch(l[0], 95, "999999"), l[1], l[2], l[3] },
            "trailer field" => new() { l[0], l[1], l[2], Patch(l[3], 1, "9").Substring(0, 394) + "00000X" },
            _ => Lines(Remessa),
        };
        Assert.ThrowsAny<CnabException>(() => CnabFile.Parse400(Reader(input)));
    }

    [Fact]
    public void Parse400AndWrite400Errors()
    {
        Assert.Equal(CnabError.Empty, Assert.Throws<CnabException>(() => CnabFile.Parse400(new StringReader(string.Empty))).Error);
        Assert.Throws<IOException>(() => CnabFile.Parse400(new FailingReader()));

        Cnab400File f = CnabFile.Parse400(new StringReader(Text(Remessa400)));
        f.Details[0].Dados = "x" + (char)10;
        Assert.Equal("record 2", Assert.Throws<CnabFieldException>(() => CnabWriter.Default.WriteToString(f)).Context);

        f = CnabFile.Parse400(new StringReader(Text(Remessa400)));
        f.Header.Banco = "x";
        Assert.Throws<CnabFieldException>(() => CnabWriter.Default.WriteToString(f));

        f = CnabFile.Parse400(new StringReader(Text(Remessa400)));
        f.Details.Clear();
        f.Trailer.Reservado = new string('x', 394);
        Assert.Throws<CnabFieldException>(() => CnabWriter.Default.WriteToString(f));
    }

    [Fact]
    public void Detect()
    {
        Assert.Equal(FileLayout.Cnab240, CnabFile.Detect(Bytes(Remessa)));
        Assert.Equal(FileLayout.Cnab400, CnabFile.Detect(Bytes(Remessa400)));
        Assert.Equal(FileLayout.Cnab240, CnabFile.Detect(new string('x', 240)));
        Assert.Equal(FileLayout.Unknown, CnabFile.Detect("abc" + (char)10));
        Assert.Equal(FileLayout.Cnab400, CnabFile.Detect(Text(Remessa400)));
        Assert.Equal("CNAB 240", FileLayout.Cnab240.Describe());
        Assert.Equal("CNAB 400", FileLayout.Cnab400.Describe());
        Assert.Equal("unknown", FileLayout.Unknown.Describe());
        Assert.Equal(
            "cnab: line 2: cnab: trailer count mismatch: detail",
            new CnabLineException(2, CnabError.Count, "detail").Message);
    }

    /// <summary>Builds a valid retorno with <paramref name="pairs"/> T/U pairs (2n+4 lines).</summary>
    internal static string BigRetorno(int pairs)
    {
        Cnab240File f = Parse(Retorno);
        Batch b = f.Batches[0];
        var t = (SegmentT)b.Segments[6];
        Segment u = b.Segments[7];
        b.Segments = new List<Segment>(2 * pairs);
        for (int i = 0; i < pairs; i++)
        {
            b.Segments.Add(t with { NossoNumero = (i + 1).ToString("D8", System.Globalization.CultureInfo.InvariantCulture) });
            b.Segments.Add(u);
        }

        return CnabWriter.Default.WriteToString(f);
    }

    [Fact]
    public void LargeFilesRoundTrip()
    {
        string data = BigRetorno(4998);
        Cnab240File f = CnabFile.Parse(new StringReader(data));
        Assert.Equal(10000, f.Trailer.QuantidadeRegistros);
        Assert.Equal(4998, f.Ocorrencias().Count);
        Assert.Equal(data, CnabWriter.Default.WriteToString(f));
    }

    private sealed class FailingReader : TextReader
    {
        public override int Read() => throw new IOException("boom");

        public override int Read(char[] buffer, int index, int count) => throw new IOException("boom");

        public override string ReadToEnd() => throw new IOException("boom");
    }
}
