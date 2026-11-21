using Cnab.Cnab240;
using static Cnab.Tests.TestData;

namespace Cnab.Tests;

public class Cnab240Tests
{
    private static DetailSegment Ctl(DetailSegment segment, int seq) =>
        segment with { Banco = "341", Lote = 1, Sequencial = seq };

    public static readonly TheoryData<string> SegmentNames = new() { "P", "Q", "R", "T", "U" };

    private static DetailSegment Sample(string code) => code switch
    {
        "P" => Ctl(
            new SegmentP
            {
                Movimento = MovimentoRemessa.Entrada, Agencia = "01234", Conta = "000000056789", ContaDV = "0",
                NossoNumero = "00000001", Carteira = 1, FormaCadastramento = 1, TipoDocumento = "1", EmissaoBoleto = 2, DistribuicaoBoleto = "2",
                NumeroDocumento = "NF-1001", Vencimento = D(2026, 12, 10), Valor = 150000, AgenciaCobradora = "00000", Especie = Especie.DuplicataMercantil,
                Aceite = "N", DataEmissao = D(2026, 11, 20), CodigoJuros = CobrancaCodes.JurosValorDiario, DataJuros = D(2026, 12, 11), Juros = 50,
                CodigoProtesto = CobrancaCodes.NaoProtestar, Moeda = CobrancaCodes.MoedaReal, NumeroContrato = "0000000000",
            },
            1),
        "Q" => Ctl(
            new SegmentQ
            {
                Movimento = MovimentoRemessa.Entrada, TipoInscricao = CobrancaCodes.InscricaoCpf, NumeroInscricao = "000052998224725",
                Nome = "JOAO DA SILVA", Endereco = "RUA A, 1", Bairro = "CENTRO", CEP = "20000", SufixoCEP = "000", Cidade = "RIO DE JANEIRO", UF = "RJ",
                InscricaoAvalista = "000000000000000", BancoCorrespondente = "000",
            },
            2),
        "R" => Ctl(
            new SegmentR
            {
                Movimento = MovimentoRemessa.Entrada, CodigoMulta = CobrancaCodes.MultaPercentual, DataMulta = D(2026, 12, 11), Multa = 200,
                Mensagem3 = "OBRIGADO", BancoDebito = "000", AgenciaDebito = "00000", ContaDebito = "000000000000",
            },
            3),
        "T" => Ctl(
            new SegmentT
            {
                Movimento = MovimentoRetorno.Liquidacao, Agencia = "01234", Conta = "000000056789", NossoNumero = "00000001",
                Carteira = 1, NumeroDocumento = "NF-1001", Vencimento = D(2026, 12, 10), Valor = 150000, BancoCobrador = "341", AgenciaCobradora = "00500",
                Moeda = 9, TipoInscricao = 1, NumeroInscricao = "000052998224725", Nome = "JOAO", NumeroContrato = "0000000000", Tarifa = 350, Motivos = "00",
            },
            4),
        _ => Ctl(
            new SegmentU
            {
                Movimento = MovimentoRetorno.Liquidacao, ValorPago = 150000, ValorLiquido = 149650,
                DataOcorrencia = D(2026, 12, 10), DataCredito = D(2026, 12, 11), BancoCorrespondente = "000",
            },
            5),
    };

    [Theory]
    [MemberData(nameof(SegmentNames))]
    public void SegmentsRoundTrip(string code)
    {
        DetailSegment segment = Sample(code);
        string line = segment.Encode();
        Assert.Equal(Layout240.LineLength, line.Length);
        Assert.Equal(segment.Code, line[13]);
        Assert.Equal(code[0], segment.Code);
        Segment decoded = Segment.Decode(line);
        Assert.Equal<Segment>(segment, decoded);
    }

    [Fact]
    public void LayoutsCoverTheWholeRecord()
    {
        FixedWidth.Check<FileHeader>(Layout240.LineLength);
        FixedWidth.Check<BatchHeader>(Layout240.LineLength);
        FixedWidth.Check<SegmentP>(Layout240.LineLength);
        FixedWidth.Check<SegmentQ>(Layout240.LineLength);
        FixedWidth.Check<SegmentR>(Layout240.LineLength);
        FixedWidth.Check<SegmentT>(Layout240.LineLength);
        FixedWidth.Check<SegmentU>(Layout240.LineLength);
        FixedWidth.Check<BatchTrailer>(Layout240.LineLength);
        FixedWidth.Check<FileTrailer>(Layout240.LineLength);
    }

    [Fact]
    public void DecodeErrors()
    {
        Assert.Equal(CnabError.LineLength, Assert.Throws<CnabFieldException>(() => Segment.Decode("x")).Error);

        string line = new SegmentP().Encode();
        string bad = Patch(line, 86, "X");
        CnabFieldException e = Assert.Throws<CnabFieldException>(() => Segment.Decode(bad));
        Assert.Equal(CnabError.Digits, e.Error);
        Assert.Equal("Valor", e.Field);

        Segment raw = Segment.Decode(new string(' ', 240));
        Assert.IsType<RawSegment>(raw);
        Assert.Equal((char)0, raw.Code);

        Assert.Equal(CnabError.RawLength, Assert.Throws<CnabException>(() => new RawSegment("x").Encode()).Error);
        Assert.Equal((char)0, new RawSegment("x").Code);

        string unknownSegment = Patch(line, 14, "Y");
        RawSegment y = Assert.IsType<RawSegment>(Segment.Decode(unknownSegment));
        Assert.Equal('Y', y.Code);
        Assert.Equal(unknownSegment, y.Encode());
    }

    [Fact]
    public void Grouping()
    {
        var p1 = new SegmentP { NossoNumero = "1" };
        var p2 = new SegmentP { NossoNumero = "2" };
        var q1 = new SegmentQ { Nome = "A" };
        var r1 = new SegmentR();
        var q2 = new SegmentQ { Nome = "B" };
        var t1 = new SegmentT { NossoNumero = "1" };
        var u1 = new SegmentU { ValorPago = 1 };
        var t2 = new SegmentT { NossoNumero = "2" };
        var batch = new Batch
        {
            Segments = new List<Segment>
            {
                new SegmentQ(), new SegmentU(), p1, q1, r1, new SegmentQ { Nome = "dup" }, p2, q2, t1, u1, new SegmentU { ValorPago = 9 }, t2, new RawSegment(string.Empty),
            },
        };

        IReadOnlyList<Titulo> titles = batch.Titulos();
        Assert.Equal(2, titles.Count);
        Assert.Same(p1, titles[0].P);
        Assert.Same(q1, titles[0].Q);
        Assert.Same(r1, titles[0].R);
        Assert.Same(p2, titles[1].P);
        Assert.Same(q2, titles[1].Q);
        Assert.Null(titles[1].R);

        IReadOnlyList<Ocorrencia> occurrences = batch.Ocorrencias();
        Assert.Equal(2, occurrences.Count);
        Assert.Same(t1, occurrences[0].T);
        Assert.Same(u1, occurrences[0].U);
        Assert.Same(t2, occurrences[1].T);
        Assert.Null(occurrences[1].U);

        var file = new Cnab240File { Batches = new List<Batch> { batch, batch } };
        Assert.Equal(4, file.Titulos().Count);
        Assert.Equal(4, file.Ocorrencias().Count);
    }

    [Fact]
    public void Codes()
    {
        Assert.Equal("Entrada de títulos", MovimentoRemessa.Entrada.Describe());
        Assert.Equal("Movimento 77", ((MovimentoRemessa)77).Describe());
        Assert.Equal("Liquidação", MovimentoRetorno.Liquidacao.Describe());
        Assert.Equal("Movimento 05", ((MovimentoRetorno)5).Describe());
        Assert.Equal("06 Liquidação", MovimentoRetorno.Liquidacao.Label());
        Assert.Equal("01 Entrada de títulos", MovimentoRemessa.Entrada.Label());

        foreach (MovimentoRetorno m in new[] { MovimentoRetorno.Liquidacao, MovimentoRetorno.LiquidacaoAposBaixa })
        {
            Assert.True(m.IsLiquidacao());
            Assert.False(m.IsRejeicao());
        }

        foreach (MovimentoRetorno m in new[] { MovimentoRetorno.EntradaRejeitada, MovimentoRetorno.InstrucaoRejeitada, MovimentoRetorno.AlteracaoRejeitada })
        {
            Assert.True(m.IsRejeicao());
            Assert.False(m.IsLiquidacao());
        }

        foreach (MovimentoRemessa m in Enum.GetValues<MovimentoRemessa>())
        {
            Assert.DoesNotContain("Movimento", m.Describe(), StringComparison.Ordinal);
        }

        foreach (MovimentoRetorno m in Enum.GetValues<MovimentoRetorno>())
        {
            Assert.DoesNotContain("Movimento", m.Describe(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("00", "")]
    [InlineData("0846", "08,46")]
    [InlineData("08  46", "08,46")]
    [InlineData("A1B", "A1,B")]
    [InlineData("0000000000", "")]
    public void MotivoCodes(string motivos, string expected)
    {
        Assert.Equal(expected, string.Join(",", new SegmentT { Motivos = motivos }.MotivoCodes()));
    }

    [Fact]
    public void DecodedRejectionSplitsItsMotivos()
    {
        string line = new SegmentT { Movimento = MovimentoRetorno.EntradaRejeitada, NossoNumero = "123", Motivos = "0846" }.Encode();
        SegmentT t = Assert.IsType<SegmentT>(Segment.Decode(line));
        Assert.Equal("Entrada rejeitada", t.Movimento.Describe());
        Assert.Equal(new[] { "08", "46" }, t.MotivoCodes());
    }

    [Fact]
    public void SegmentPPositionsMatchTheManual()
    {
        var p = new SegmentP
        {
            Banco = "341", Lote = 1, Sequencial = 1, Movimento = MovimentoRemessa.Entrada, Agencia = "01234",
            Conta = "000000056789", ContaDV = "0", NossoNumero = "00000001", Carteira = 1, NumeroDocumento = "NF-1001",
            Vencimento = D(2026, 12, 10), Valor = 150000, Especie = Especie.DuplicataMercantil, Aceite = "N", Moeda = CobrancaCodes.MoedaReal,
        };
        string line = FixedWidth.Serialize(p, Layout240.LineLength);
        Assert.Equal("3410001300001P 01", line.Substring(0, 17));
        Assert.Equal("00000001            ", line.Substring(37, 20));
        Assert.Equal("10122026", line.Substring(77, 8));
        Assert.Equal("000000000150000", line.Substring(85, 15));
    }
}
