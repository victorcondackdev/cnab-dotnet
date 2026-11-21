namespace Cnab.Cnab400.Bradesco;

/// <summary>
/// The Banco Bradesco (237) CNAB 400 cobrança remessa detail record (type 1),
/// an example of a bank-specific layout built on <see cref="FixedWidth"/>.
/// </summary>
/// <remarks>
/// EXPERIMENTAL: the positions follow the publicly circulated Bradesco
/// "Cobrança Bancária - CNAB 400" remessa manual. They have not been
/// homologated against the bank; validate them with your account manager
/// before production use. The retorno layout is not declared yet.
/// </remarks>
public sealed record DetalheRemessa
{
    /// <summary>Bradesco's compensation code.</summary>
    public const string Banco = "237";

    /// <summary>1: tipo de registro, always 1.</summary>
    [CnabField(1, 1, CnabFormat.Const, Value = "1")]
    public string Registro { get; set; } = "1";

    /// <summary>2-6: agência de débito (débito automático).</summary>
    [CnabField(2, 5, CnabFormat.Num)]
    public string AgenciaDebito { get; set; } = string.Empty;

    /// <summary>7: dígito da agência de débito.</summary>
    [CnabField(7, 1, CnabFormat.Alfa)]
    public string AgenciaDebitoDV { get; set; } = string.Empty;

    /// <summary>8-12: razão da conta corrente de débito.</summary>
    [CnabField(8, 5, CnabFormat.Num)]
    public string RazaoContaDebito { get; set; } = string.Empty;

    /// <summary>13-19: conta corrente de débito.</summary>
    [CnabField(13, 7, CnabFormat.Num)]
    public string ContaDebito { get; set; } = string.Empty;

    /// <summary>20: dígito da conta de débito.</summary>
    [CnabField(20, 1, CnabFormat.Alfa)]
    public string ContaDebitoDV { get; set; } = string.Empty;

    /// <summary>21-37: identificação da empresa: 0 + carteira (3) + agência (5) + conta (7) + dígito.</summary>
    [CnabField(21, 17, CnabFormat.Alfa)]
    public string IdentificacaoEmpresa { get; set; } = string.Empty;

    /// <summary>38-62: número de controle do participante (uso da empresa).</summary>
    [CnabField(38, 25, CnabFormat.Alfa)]
    public string ControleParticipante { get; set; } = string.Empty;

    /// <summary>63-65: código do banco a ser debitado na câmara de compensação.</summary>
    [CnabField(63, 3, CnabFormat.Num)]
    public string BancoDebito { get; set; } = string.Empty;

    /// <summary>66: campo de multa (0 sem multa, 2 com multa).</summary>
    [CnabField(66, 1, CnabFormat.Num)]
    public int CampoMulta { get; set; }

    /// <summary>67-70: percentual de multa, two implied decimals.</summary>
    [CnabField(67, 4, CnabFormat.Num)]
    public int PercentualMulta { get; set; }

    /// <summary>71-81: identificação do título no banco.</summary>
    [CnabField(71, 11, CnabFormat.Num)]
    public string NossoNumero { get; set; } = string.Empty;

    /// <summary>82: dígito de auto conferência do nosso número.</summary>
    [CnabField(82, 1, CnabFormat.Alfa)]
    public string NossoNumeroDV { get; set; } = string.Empty;

    /// <summary>83-92: desconto bonificação por dia (cents).</summary>
    [CnabField(83, 10, CnabFormat.Money)]
    public long DescontoDia { get; set; }

    /// <summary>93: condição para emissão do boleto (1 banco emite, 2 cliente emite).</summary>
    [CnabField(93, 1, CnabFormat.Num)]
    public int CondicaoEmissao { get; set; }

    /// <summary>94: identificação se emite boleto para débito automático.</summary>
    [CnabField(94, 1, CnabFormat.Alfa)]
    public string DebitoAutomatico { get; set; } = string.Empty;

    /// <summary>95-104: identificação da operação do banco.</summary>
    [CnabField(95, 10, CnabFormat.Alfa)]
    public string OperacaoBanco { get; set; } = string.Empty;

    /// <summary>105: indicador de rateio de crédito.</summary>
    [CnabField(105, 1, CnabFormat.Alfa)]
    public string IndicadorRateio { get; set; } = string.Empty;

    /// <summary>106: endereçamento para aviso do débito automático.</summary>
    [CnabField(106, 1, CnabFormat.Alfa)]
    public string AvisoDebito { get; set; } = string.Empty;

    /// <summary>107-108: quantidade de pagamentos.</summary>
    [CnabField(107, 2, CnabFormat.Alfa)]
    public string QuantidadePagamentos { get; set; } = string.Empty;

    /// <summary>109-110: identificação da ocorrência (01 remessa, 02 pedido de baixa, ...).</summary>
    [CnabField(109, 2, CnabFormat.Num)]
    public int Ocorrencia { get; set; }

    /// <summary>111-120: número do documento.</summary>
    [CnabField(111, 10, CnabFormat.Alfa)]
    public string NumeroDocumento { get; set; } = string.Empty;

    /// <summary>121-126: data do vencimento do título (DDMMYY).</summary>
    [CnabField(121, 6, CnabFormat.Date)]
    public DateOnly? Vencimento { get; set; }

    /// <summary>127-139: valor do título (cents).</summary>
    [CnabField(127, 13, CnabFormat.Money)]
    public long Valor { get; set; }

    /// <summary>140-142: banco encarregado da cobrança.</summary>
    [CnabField(140, 3, CnabFormat.Num)]
    public string BancoCobranca { get; set; } = string.Empty;

    /// <summary>143-147: agência depositária.</summary>
    [CnabField(143, 5, CnabFormat.Num)]
    public string AgenciaDepositaria { get; set; } = string.Empty;

    /// <summary>148-149: espécie de título.</summary>
    [CnabField(148, 2, CnabFormat.Num)]
    public int Especie { get; set; }

    /// <summary>150: identificação, always N.</summary>
    [CnabField(150, 1, CnabFormat.Alfa)]
    public string Identificacao { get; set; } = string.Empty;

    /// <summary>151-156: data da emissão do título (DDMMYY).</summary>
    [CnabField(151, 6, CnabFormat.Date)]
    public DateOnly? DataEmissao { get; set; }

    /// <summary>157-158: primeira instrução.</summary>
    [CnabField(157, 2, CnabFormat.Num)]
    public int Instrucao1 { get; set; }

    /// <summary>159-160: segunda instrução.</summary>
    [CnabField(159, 2, CnabFormat.Num)]
    public int Instrucao2 { get; set; }

    /// <summary>161-173: valor a ser cobrado por dia de atraso (cents).</summary>
    [CnabField(161, 13, CnabFormat.Money)]
    public long MoraDia { get; set; }

    /// <summary>174-179: data limite para concessão de desconto.</summary>
    [CnabField(174, 6, CnabFormat.Date)]
    public DateOnly? DataLimiteDesconto { get; set; }

    /// <summary>180-192: valor do desconto (cents).</summary>
    [CnabField(180, 13, CnabFormat.Money)]
    public long Desconto { get; set; }

    /// <summary>193-205: valor do IOF (cents).</summary>
    [CnabField(193, 13, CnabFormat.Money)]
    public long IOF { get; set; }

    /// <summary>206-218: valor do abatimento (cents).</summary>
    [CnabField(206, 13, CnabFormat.Money)]
    public long Abatimento { get; set; }

    /// <summary>219-220: tipo de inscrição do pagador (01 CPF, 02 CNPJ).</summary>
    [CnabField(219, 2, CnabFormat.Num)]
    public int TipoInscricao { get; set; }

    /// <summary>221-234: número de inscrição do pagador.</summary>
    [CnabField(221, 14, CnabFormat.Num)]
    public string NumeroInscricao { get; set; } = string.Empty;

    /// <summary>235-274: nome do pagador.</summary>
    [CnabField(235, 40, CnabFormat.Alfa)]
    public string Nome { get; set; } = string.Empty;

    /// <summary>275-314: endereço completo.</summary>
    [CnabField(275, 40, CnabFormat.Alfa)]
    public string Endereco { get; set; } = string.Empty;

    /// <summary>315-326: primeira mensagem.</summary>
    [CnabField(315, 12, CnabFormat.Alfa)]
    public string Mensagem1 { get; set; } = string.Empty;

    /// <summary>327-331: CEP.</summary>
    [CnabField(327, 5, CnabFormat.Num)]
    public string CEP { get; set; } = string.Empty;

    /// <summary>332-334: sufixo do CEP.</summary>
    [CnabField(332, 3, CnabFormat.Num)]
    public string SufixoCEP { get; set; } = string.Empty;

    /// <summary>335-394: sacador/avalista ou segunda mensagem.</summary>
    [CnabField(335, 60, CnabFormat.Alfa)]
    public string SacadorOuMensagem2 { get; set; } = string.Empty;

    /// <summary>395-400: número sequencial do registro.</summary>
    [CnabField(395, 6, CnabFormat.Num)]
    public int Sequencial { get; set; }
}
