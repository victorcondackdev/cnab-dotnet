using Cnab.Cnab400;
using Cnab.Cnab400.Bradesco;
using static Cnab.Tests.TestData;

namespace Cnab.Tests;

public class Cnab400Tests
{
    [Fact]
    public void LayoutsCoverTheWholeRecord()
    {
        FixedWidth.Check<Header>(Layout400.LineLength);
        FixedWidth.Check<Detail>(Layout400.LineLength);
        FixedWidth.Check<Trailer>(Layout400.LineLength);
        FixedWidth.Check<DetalheRemessa>(Layout400.LineLength);
    }

    [Fact]
    public void BradescoDetailRoundTrip()
    {
        var want = new DetalheRemessa
        {
            AgenciaDebito = "00000",
            AgenciaDebitoDV = "0",
            RazaoContaDebito = "00000",
            ContaDebito = "0000000",
            ContaDebitoDV = "0",
            IdentificacaoEmpresa = "00090123400123456",
            ControleParticipante = "PEDIDO 1001",
            BancoDebito = "000",
            CampoMulta = 2,
            PercentualMulta = 200,
            NossoNumero = "00000000123",
            NossoNumeroDV = "P",
            CondicaoEmissao = 2,
            DebitoAutomatico = "N",
            Ocorrencia = 1,
            NumeroDocumento = "NF1001",
            Vencimento = D(2026, 12, 10),
            Valor = 150000,
            BancoCobranca = "000",
            AgenciaDepositaria = "00000",
            Especie = 1,
            Identificacao = "N",
            DataEmissao = D(2026, 11, 20),
            MoraDia = 50,
            TipoInscricao = 1,
            NumeroInscricao = "00052998224725",
            Nome = "MARIA DA SILVA",
            Endereco = "RUA DAS FLORES 100",
            CEP = "20000",
            SufixoCEP = "000",
            Sequencial = 2,
        };
        string line = FixedWidth.Serialize(want, Layout400.LineLength);
        Assert.Equal("101226", line.Substring(120, 6));
        Assert.Equal("0000000150000", line.Substring(126, 13));

        Detail detail = FixedWidth.Deserialize<Detail>(line);
        Assert.Equal(line, detail.ToLine());
        Assert.Equal(want, detail.Decode<DetalheRemessa>());
        Assert.Equal("237", DetalheRemessa.Banco);
    }

    [Fact]
    public void DetailDecodeErrors()
    {
        var withNewline = new Detail { Registro = "1", Dados = "x" + (char)10 };
        Assert.ThrowsAny<CnabException>(() => withNewline.Decode<DetalheRemessa>());

        var typeSeven = new Detail { Registro = "7" };
        CnabFieldException e = Assert.Throws<CnabFieldException>(() => typeSeven.Decode<DetalheRemessa>());
        Assert.Equal(CnabError.Const, e.Error);
        Assert.Equal("Registro", e.Field);
    }
}
