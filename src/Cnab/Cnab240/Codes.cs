using Cnab.Internal;

namespace Cnab.Cnab240;

/// <summary>
/// The "código de movimento remessa" (positions 16-17 of segments P, Q and R):
/// the instruction the company sends to the bank.
/// </summary>
/// <remarks>
/// Only the codes defined identically by the FEBRABAN generic layout are
/// declared. Banks accept subsets and add their own codes; any two-digit value
/// round-trips.
/// </remarks>
public enum MovimentoRemessa
{
    /// <summary>01: entrada de títulos.</summary>
    Entrada = 1,

    /// <summary>02: pedido de baixa.</summary>
    Baixa = 2,

    /// <summary>04: concessão de abatimento.</summary>
    Abatimento = 4,

    /// <summary>05: cancelamento de abatimento.</summary>
    CancelaAbatimento = 5,

    /// <summary>06: alteração de vencimento.</summary>
    AlteracaoVencimento = 6,

    /// <summary>07: concessão de desconto.</summary>
    Desconto = 7,

    /// <summary>08: cancelamento de desconto.</summary>
    CancelaDesconto = 8,

    /// <summary>09: protestar.</summary>
    Protestar = 9,

    /// <summary>10: sustar protesto e baixar título.</summary>
    SustarProtestoBaixar = 10,

    /// <summary>11: sustar protesto e manter em carteira.</summary>
    SustarProtestoManter = 11,

    /// <summary>31: alteração de outros dados.</summary>
    AlteracaoOutrosDados = 31,
}

/// <summary>
/// The "código de movimento retorno" (positions 16-17 of segments T and U):
/// the occurrence the bank reports back.
/// </summary>
/// <remarks>
/// Only codes with the same meaning across the FEBRABAN generic layout are
/// declared. Banks add their own codes; check the manual for anything not
/// listed here.
/// </remarks>
public enum MovimentoRetorno
{
    /// <summary>02: entrada confirmada.</summary>
    EntradaConfirmada = 2,

    /// <summary>03: entrada rejeitada.</summary>
    EntradaRejeitada = 3,

    /// <summary>06: liquidação.</summary>
    Liquidacao = 6,

    /// <summary>09: baixa.</summary>
    Baixa = 9,

    /// <summary>12: confirmação de recebimento de instrução de abatimento.</summary>
    ConfirmacaoAbatimento = 12,

    /// <summary>13: confirmação de cancelamento de abatimento.</summary>
    ConfirmacaoCancelamentoAbatimento = 13,

    /// <summary>14: confirmação de alteração de vencimento.</summary>
    ConfirmacaoVencimento = 14,

    /// <summary>17: liquidação após baixa ou de título não registrado.</summary>
    LiquidacaoAposBaixa = 17,

    /// <summary>19: confirmação de recebimento de instrução de protesto.</summary>
    ConfirmacaoProtesto = 19,

    /// <summary>20: confirmação de recebimento de instrução de sustação de protesto.</summary>
    ConfirmacaoSustacao = 20,

    /// <summary>23: remessa a cartório.</summary>
    RemessaCartorio = 23,

    /// <summary>24: retirada de cartório e manutenção em carteira.</summary>
    RetiradaCartorio = 24,

    /// <summary>25: protestado e baixado.</summary>
    ProtestadoBaixado = 25,

    /// <summary>26: instrução rejeitada.</summary>
    InstrucaoRejeitada = 26,

    /// <summary>27: confirmação do pedido de alteração de outros dados.</summary>
    ConfirmacaoOutrosDados = 27,

    /// <summary>28: débito de tarifas/custas.</summary>
    DebitoTarifas = 28,

    /// <summary>30: alteração de dados rejeitada.</summary>
    AlteracaoRejeitada = 30,
}

/// <summary>
/// The "espécie do título" (positions 107-108 of segment P). Only a few widely
/// used codes are declared; the full table is in the FEBRABAN manual and banks
/// restrict it further.
/// </summary>
public enum Especie
{
    /// <summary>02: duplicata mercantil (DM).</summary>
    DuplicataMercantil = 2,

    /// <summary>04: duplicata de serviço (DS).</summary>
    DuplicataServico = 4,

    /// <summary>12: nota promissória (NP).</summary>
    NotaPromissoria = 12,

    /// <summary>17: recibo (RC).</summary>
    Recibo = 17,

    /// <summary>99: outros.</summary>
    Outros = 99,
}

/// <summary>Portuguese descriptions and classification helpers for the CNAB 240 code enums.</summary>
public static class CodeExtensions
{
    /// <summary>Returns the Portuguese description, or <c>Movimento NN</c> for codes not declared.</summary>
    /// <param name="code">The movement code.</param>
    /// <returns>The description.</returns>
    public static string Describe(this MovimentoRemessa code) => code switch
    {
        MovimentoRemessa.Entrada => "Entrada de títulos",
        MovimentoRemessa.Baixa => "Pedido de baixa",
        MovimentoRemessa.Abatimento => "Concessão de abatimento",
        MovimentoRemessa.CancelaAbatimento => "Cancelamento de abatimento",
        MovimentoRemessa.AlteracaoVencimento => "Alteração de vencimento",
        MovimentoRemessa.Desconto => "Concessão de desconto",
        MovimentoRemessa.CancelaDesconto => "Cancelamento de desconto",
        MovimentoRemessa.Protestar => "Protestar",
        MovimentoRemessa.SustarProtestoBaixar => "Sustar protesto e baixar título",
        MovimentoRemessa.SustarProtestoManter => "Sustar protesto e manter em carteira",
        MovimentoRemessa.AlteracaoOutrosDados => "Alteração de outros dados",
        _ => Unknown((int)code),
    };

    /// <summary>Returns the Portuguese description, or <c>Movimento NN</c> for codes not declared.</summary>
    /// <param name="code">The movement code.</param>
    /// <returns>The description.</returns>
    public static string Describe(this MovimentoRetorno code) => code switch
    {
        MovimentoRetorno.EntradaConfirmada => "Entrada confirmada",
        MovimentoRetorno.EntradaRejeitada => "Entrada rejeitada",
        MovimentoRetorno.Liquidacao => "Liquidação",
        MovimentoRetorno.Baixa => "Baixa",
        MovimentoRetorno.ConfirmacaoAbatimento => "Confirmação de abatimento",
        MovimentoRetorno.ConfirmacaoCancelamentoAbatimento => "Confirmação de cancelamento de abatimento",
        MovimentoRetorno.ConfirmacaoVencimento => "Confirmação de alteração de vencimento",
        MovimentoRetorno.LiquidacaoAposBaixa => "Liquidação após baixa ou título não registrado",
        MovimentoRetorno.ConfirmacaoProtesto => "Confirmação de instrução de protesto",
        MovimentoRetorno.ConfirmacaoSustacao => "Confirmação de sustação de protesto",
        MovimentoRetorno.RemessaCartorio => "Remessa a cartório",
        MovimentoRetorno.RetiradaCartorio => "Retirada de cartório",
        MovimentoRetorno.ProtestadoBaixado => "Protestado e baixado",
        MovimentoRetorno.InstrucaoRejeitada => "Instrução rejeitada",
        MovimentoRetorno.ConfirmacaoOutrosDados => "Confirmação de alteração de outros dados",
        MovimentoRetorno.DebitoTarifas => "Débito de tarifas/custas",
        MovimentoRetorno.AlteracaoRejeitada => "Alteração de dados rejeitada",
        _ => Unknown((int)code),
    };

    /// <summary>Reports whether the occurrence settles the title (06 or 17).</summary>
    /// <param name="code">The movement code.</param>
    /// <returns><see langword="true"/> for a settlement.</returns>
    public static bool IsLiquidacao(this MovimentoRetorno code) =>
        code is MovimentoRetorno.Liquidacao or MovimentoRetorno.LiquidacaoAposBaixa;

    /// <summary>Reports whether the occurrence rejects an instruction (03, 26 or 30).</summary>
    /// <param name="code">The movement code.</param>
    /// <returns><see langword="true"/> for a rejection.</returns>
    public static bool IsRejeicao(this MovimentoRetorno code) =>
        code is MovimentoRetorno.EntradaRejeitada or MovimentoRetorno.InstrucaoRejeitada or MovimentoRetorno.AlteracaoRejeitada;

    /// <summary>Formats a code as two digits followed by its description, for example <c>06 Liquidação</c>.</summary>
    /// <param name="code">The movement code.</param>
    /// <returns>The label.</returns>
    public static string Label(this MovimentoRetorno code) => Two((int)code) + " " + code.Describe();

    /// <summary>Formats a code as two digits followed by its description, for example <c>01 Entrada de títulos</c>.</summary>
    /// <param name="code">The movement code.</param>
    /// <returns>The label.</returns>
    public static string Label(this MovimentoRemessa code) => Two((int)code) + " " + code.Describe();

    private static string Unknown(int code) => "Movimento " + Two(code);

    private static string Two(int code) => code.ToString("D2", GoText.Inv);
}

/// <summary>
/// Numeric codes of the CNAB 240 cobrança layout that are plain integers in
/// the records. Names follow the FEBRABAN manual.
/// </summary>
public static class CobrancaCodes
{
    /// <summary>Tipo de inscrição: isento / não informado.</summary>
    public const int InscricaoIsento = 0;

    /// <summary>Tipo de inscrição: CPF.</summary>
    public const int InscricaoCpf = 1;

    /// <summary>Tipo de inscrição: CNPJ.</summary>
    public const int InscricaoCnpj = 2;

    /// <summary>Código remessa/retorno of the file header (position 143): remessa.</summary>
    public const int CodigoRemessa = 1;

    /// <summary>Código remessa/retorno of the file header (position 143): retorno.</summary>
    public const int CodigoRetorno = 2;

    /// <summary>Operação of the batch header: remessa.</summary>
    public const string OperacaoRemessa = "R";

    /// <summary>Operação of the batch header: retorno.</summary>
    public const string OperacaoRetorno = "T";

    /// <summary>Tipo de serviço of the batch header: cobrança.</summary>
    public const int ServicoCobranca = 1;

    /// <summary>Juros (segment P, position 118): valor por dia.</summary>
    public const int JurosValorDiario = 1;

    /// <summary>Juros (segment P, position 118): taxa mensal.</summary>
    public const int JurosTaxaMensal = 2;

    /// <summary>Juros (segment P, position 118): isento.</summary>
    public const int JurosIsento = 3;

    /// <summary>Desconto (segments P and R): sem desconto.</summary>
    public const int SemDesconto = 0;

    /// <summary>Desconto (segments P and R): valor fixo até a data informada.</summary>
    public const int DescontoValorFixo = 1;

    /// <summary>Desconto (segments P and R): percentual até a data informada.</summary>
    public const int DescontoPercentual = 2;

    /// <summary>Multa (segment R, position 66): sem multa.</summary>
    public const int SemMulta = 0;

    /// <summary>Multa (segment R, position 66): valor fixo.</summary>
    public const int MultaValorFixo = 1;

    /// <summary>Multa (segment R, position 66): percentual.</summary>
    public const int MultaPercentual = 2;

    /// <summary>Protesto (segment P, position 221): protestar dias corridos.</summary>
    public const int ProtestarDiasCorridos = 1;

    /// <summary>Protesto (segment P, position 221): não protestar.</summary>
    public const int NaoProtestar = 3;

    /// <summary>Baixa (segment P, position 224): baixar / devolver.</summary>
    public const int BaixarDevolver = 1;

    /// <summary>Baixa (segment P, position 224): não baixar / não devolver.</summary>
    public const int NaoBaixar = 2;

    /// <summary>Moeda (segment P, positions 228-229): real.</summary>
    public const int MoedaReal = 9;
}

/// <summary>Record type codes (position 8, "tipo de registro").</summary>
public static class RecordType
{
    /// <summary>File header.</summary>
    public const char HeaderArquivo = '0';

    /// <summary>Batch header.</summary>
    public const char HeaderLote = '1';

    /// <summary>Initial batch records.</summary>
    public const char IniciaisLote = '2';

    /// <summary>Detail segment.</summary>
    public const char Detalhe = '3';

    /// <summary>Final batch records.</summary>
    public const char FinaisLote = '4';

    /// <summary>Batch trailer.</summary>
    public const char TrailerLote = '5';

    /// <summary>File trailer.</summary>
    public const char TrailerArquivo = '9';
}
