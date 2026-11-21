using Cnab.Cnab240;
using Cnab.Remessa;
using Cnab.Tests.Parity;
using static Cnab.Tests.TestData;

namespace Cnab.Tests;

public class RemessaBuilderTests
{
    private static readonly Company TestCompany = new()
    {
        Nome = "Acme Utilidades Ltda", Documento = "11.222.333/0001-81", Agencia = "01234", Conta = "000000056789", ContaDV = "0",
    };

    private static readonly Bank TestBank = new() { Codigo = "341", Nome = "Banco Itaú S.A." };

    private static readonly Pagador Joao = new()
    {
        Nome = "João da Silva Sauro", Documento = "529.982.247-25", Endereco = "Rua das Laranjeiras, 1500 apto 302",
        Bairro = "Laranjeiras", CEP = "22240-003", Cidade = "Rio de Janeiro", UF = "RJ",
    };

    private static readonly Boleto Ok = new()
    {
        NossoNumero = "1", NumeroDocumento = "X", Vencimento = D(2026, 1, 2), Valor = 100, Pagador = Joao,
    };

    [Fact]
    public void BuilderMatchesTheSample()
    {
        RemessaBuilder builder = ParityTests.SampleBuilder();
        var stream = new MemoryStream();
        long n = builder.WriteTo(stream);
        byte[] want = Bytes("remessa_341.rem");
        Assert.Equal(want.Length, n);
        Assert.Equal(want, stream.ToArray());

        var writer = new StringWriter();
        ParityTests.SampleBuilder().WriteTo(writer);
        Assert.Equal(Text("remessa_341.rem"), writer.ToString());
    }

    [Fact]
    public void BuilderFields()
    {
        Cnab240File f = ParityTests.SampleBuilder().Build();
        IReadOnlyList<Titulo> ts = f.Titulos();
        SegmentP p = ts[1].P;
        Assert.Equal(CobrancaCodes.JurosValorDiario, p.CodigoJuros);
        Assert.Equal(D(2026, 12, 16), p.DataJuros);
        Assert.Equal(CobrancaCodes.ProtestarDiasCorridos, p.CodigoProtesto);
        Assert.Equal(5, p.DiasProtesto);
        Assert.Equal(CobrancaCodes.DescontoValorFixo, ts[3].P.CodigoDesconto);
        Assert.Equal(D(2026, 12, 20), ts[3].P.DataDesconto);
        Assert.Equal(CobrancaCodes.InscricaoCnpj, ts[1].Q!.TipoInscricao);
        Assert.Equal(CobrancaCodes.InscricaoCpf, ts[0].Q!.TipoInscricao);
        Assert.Equal(13, f.Batches[0].Trailer.QuantidadeRegistros);
        Assert.Equal(15, f.Trailer.QuantidadeRegistros);

        // Defaults: emissão = remessa date, carteira 1, aceite, baixa, layout versions.
        Cnab240File g = new RemessaBuilder(TestCompany, new Bank { Codigo = "001", VersaoArquivo = "084", VersaoLote = "042" })
            .AddBoleto(Ok with { Aceite = true, DiasBaixa = 30, UsoEmpresa = "pedido 9" })
            .Build();
        p = g.Titulos()[0].P;
        Assert.NotNull(p.DataEmissao);
        Assert.Equal(1, p.Carteira);
        Assert.Equal("A", p.Aceite);
        Assert.Equal(1, p.CodigoBaixa);
        Assert.Equal("030", p.DiasBaixa);
        Assert.Equal("PEDIDO 9", p.UsoEmpresa);
        Assert.Equal("084", g.Header.VersaoLayout);
        Assert.Equal("042", g.Batches[0].Header.VersaoLayout);
    }

    public static readonly TheoryData<string> BoletoErrorCases = new()
    {
        "nosso número", "nn long", "valor", "vencimento", "documento", "doc charset", "negative", "protesto",
        "cpf", "cep", "cep letters", "nome", "uf", "pagador text", "uso empresa", "mensagem",
    };

    [Theory]
    [MemberData(nameof(BoletoErrorCases))]
    public void InvalidBoletosAreReportedWithTheirIndex(string name)
    {
        string control = ((char)1).ToString();
        Boleto bad = name switch
        {
            "nosso número" => Ok with { NossoNumero = string.Empty },
            "nn long" => Ok with { NossoNumero = new string('1', 21) },
            "valor" => Ok with { Valor = 0 },
            "vencimento" => Ok with { Vencimento = null },
            "documento" => Ok with { NumeroDocumento = string.Empty },
            "doc charset" => Ok with { NumeroDocumento = "€" },
            "negative" => Ok with { Desconto = -1 },
            "protesto" => Ok with { DiasProtesto = 100 },
            "cpf" => Ok with { Pagador = Joao with { Documento = "52998224724" } },
            "cep" => Ok with { Pagador = Joao with { CEP = "2224000" } },
            "cep letters" => Ok with { Pagador = Joao with { CEP = "2224000A" } },
            "nome" => Ok with { Pagador = Joao with { Nome = string.Empty } },
            "uf" => Ok with { Pagador = Joao with { UF = "R" } },
            "pagador text" => Ok with { Pagador = Joao with { Cidade = "東京" } },
            "uso empresa" => Ok with { UsoEmpresa = control },
            _ => Ok with { Mensagem = control },
        };
        var builder = new RemessaBuilder(TestCompany, TestBank).AddBoleto(Ok).AddBoleto(bad);
        Assert.Equal(1, builder.Count);
        RemessaException e = Assert.Throws<RemessaException>(() => builder.Build());
        Assert.Equal(CnabError.InvalidRemessa, e.Error);
        Assert.StartsWith("boleto 2 ", Assert.Single(e.Problems), StringComparison.Ordinal);
    }

    public static readonly TheoryData<string> BuildErrorCases = new()
    {
        "empty", "company doc", "bank code", "company name", "bank name", "agencia",
    };

    [Theory]
    [MemberData(nameof(BuildErrorCases))]
    public void InvalidCompanyOrBankIsReported(string name)
    {
        RemessaBuilder builder = name switch
        {
            "empty" => new RemessaBuilder(TestCompany, TestBank),
            "company doc" => new RemessaBuilder(new Company { Nome = "X", Documento = "1" }, TestBank).AddBoleto(Ok),
            "bank code" => new RemessaBuilder(TestCompany, new Bank { Codigo = "34" }).AddBoleto(Ok),
            "company name" => new RemessaBuilder(new Company { Documento = TestCompany.Documento }, TestBank).AddBoleto(Ok),
            "bank name" => new RemessaBuilder(TestCompany, new Bank { Codigo = "341", Nome = ((char)0).ToString() }).AddBoleto(Ok),
            _ => new RemessaBuilder(new Company { Nome = "X", Documento = TestCompany.Documento, Agencia = "12a" }, TestBank).AddBoleto(Ok),
        };
        RemessaException e = Assert.Throws<RemessaException>(() => builder.Build());
        Assert.Equal(CnabError.InvalidRemessa, e.Error);
        Assert.StartsWith("cnab: invalid remessa data: ", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FieldErrorsFoundWhileEncodingAreWrapped()
    {
        RemessaException e = Assert.Throws<RemessaException>(() =>
            new RemessaBuilder(new Company { Nome = "X", Documento = TestCompany.Documento, Agencia = "12a" }, TestBank).AddBoleto(Ok).Build());
        Assert.Equal(
            "cnab: invalid remessa data: file header: cnab: field Agencia (53-57) value \"12a\": must contain only digits",
            e.Message);
        Assert.IsType<CnabFieldException>(e.InnerException);
    }

    [Fact]
    public void DefaultsAndTruncation()
    {
        Assert.Throws<RemessaException>(() => new RemessaBuilder(TestCompany, new Bank()).WriteTo(new MemoryStream()));

        // Generation time defaults to now.
        Cnab240File f = new RemessaBuilder(TestCompany, TestBank).AddBoleto(Ok).Build();
        Assert.NotNull(f.Header.DataGeracao);

        // Long text is truncated, not rejected.
        f = new RemessaBuilder(TestCompany, TestBank)
            .AddBoletos(new[] { Ok with { Pagador = Joao with { Nome = new string('Á', 50) } } })
            .Build();
        Assert.Equal(new string('A', 40), f.Titulos()[0].Q!.Nome);
    }
}
