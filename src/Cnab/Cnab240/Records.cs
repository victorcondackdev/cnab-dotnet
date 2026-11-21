namespace Cnab.Cnab240;

/// <summary>Constants of the CNAB 240 layout.</summary>
public static class Layout240
{
    /// <summary>The length in characters of every CNAB 240 record.</summary>
    public const int LineLength = 240;
}

/// <summary>
/// The file header, record type 0 ("header de arquivo"). Positions follow the
/// FEBRABAN generic layout for cobrança (versions 08.x to 10.x).
/// </summary>
/// <remarks>
/// <see cref="VersaoLayout"/> (164-166) is bank- and version-specific: check
/// the manual. <see cref="ReservadoBanco"/> (172-191) is used by several banks
/// for their own data.
/// </remarks>
public sealed record FileHeader
{
    /// <summary>1-3: código do banco na compensação.</summary>
    [CnabField(1, 3, CnabFormat.Num)]
    public string Banco { get; set; } = string.Empty;

    /// <summary>4-7: lote de serviço (0000 in the file header).</summary>
    [CnabField(4, 4, CnabFormat.Num)]
    public int Lote { get; set; }

    /// <summary>8: tipo de registro, always 0.</summary>
    [CnabField(8, 1, CnabFormat.Const, Value = "0")]
    public string Registro { get; set; } = "0";

    /// <summary>9-17: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(9, 9, CnabFormat.Alfa)]
    public string CNAB1 { get; set; } = string.Empty;

    /// <summary>18: tipo de inscrição da empresa (1 CPF, 2 CNPJ).</summary>
    [CnabField(18, 1, CnabFormat.Num)]
    public int TipoInscricao { get; set; }

    /// <summary>19-32: número de inscrição da empresa.</summary>
    [CnabField(19, 14, CnabFormat.Num)]
    public string NumeroInscricao { get; set; } = string.Empty;

    /// <summary>33-52: código do convênio no banco.</summary>
    [CnabField(33, 20, CnabFormat.Alfa)]
    public string Convenio { get; set; } = string.Empty;

    /// <summary>53-57: agência mantenedora da conta.</summary>
    [CnabField(53, 5, CnabFormat.Num)]
    public string Agencia { get; set; } = string.Empty;

    /// <summary>58: dígito verificador da agência.</summary>
    [CnabField(58, 1, CnabFormat.Alfa)]
    public string AgenciaDV { get; set; } = string.Empty;

    /// <summary>59-70: número da conta corrente.</summary>
    [CnabField(59, 12, CnabFormat.Num)]
    public string Conta { get; set; } = string.Empty;

    /// <summary>71: dígito verificador da conta.</summary>
    [CnabField(71, 1, CnabFormat.Alfa)]
    public string ContaDV { get; set; } = string.Empty;

    /// <summary>72: dígito verificador da agência/conta.</summary>
    [CnabField(72, 1, CnabFormat.Alfa)]
    public string AgenciaContaDV { get; set; } = string.Empty;

    /// <summary>73-102: nome da empresa.</summary>
    [CnabField(73, 30, CnabFormat.Alfa)]
    public string NomeEmpresa { get; set; } = string.Empty;

    /// <summary>103-132: nome do banco.</summary>
    [CnabField(103, 30, CnabFormat.Alfa)]
    public string NomeBanco { get; set; } = string.Empty;

    /// <summary>133-142: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(133, 10, CnabFormat.Alfa)]
    public string CNAB2 { get; set; } = string.Empty;

    /// <summary>143: código remessa (1) / retorno (2).</summary>
    [CnabField(143, 1, CnabFormat.Num)]
    public int CodigoRemessaRetorno { get; set; }

    /// <summary>144-151: data de geração do arquivo.</summary>
    [CnabField(144, 8, CnabFormat.Date)]
    public DateOnly? DataGeracao { get; set; }

    /// <summary>152-157: hora de geração do arquivo (HHMMSS).</summary>
    [CnabField(152, 6, CnabFormat.Num)]
    public string HoraGeracao { get; set; } = string.Empty;

    /// <summary>158-163: número sequencial do arquivo.</summary>
    [CnabField(158, 6, CnabFormat.Num)]
    public int NSA { get; set; }

    /// <summary>164-166: número da versão do layout do arquivo.</summary>
    [CnabField(164, 3, CnabFormat.Num)]
    public string VersaoLayout { get; set; } = string.Empty;

    /// <summary>167-171: densidade de gravação do arquivo.</summary>
    [CnabField(167, 5, CnabFormat.Num)]
    public string Densidade { get; set; } = string.Empty;

    /// <summary>172-191: para uso reservado do banco.</summary>
    [CnabField(172, 20, CnabFormat.Alfa)]
    public string ReservadoBanco { get; set; } = string.Empty;

    /// <summary>192-211: para uso reservado da empresa.</summary>
    [CnabField(192, 20, CnabFormat.Alfa)]
    public string ReservadoEmpresa { get; set; } = string.Empty;

    /// <summary>212-240: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(212, 29, CnabFormat.Alfa)]
    public string CNAB3 { get; set; } = string.Empty;
}

/// <summary>
/// The cobrança batch header, record type 1 ("header de lote").
/// <see cref="Operacao"/> is <c>R</c> for remessa and <c>T</c> for retorno;
/// <see cref="Servico"/> is 01 (cobrança). The company inscription has 15
/// digits here, one more than in the file header.
/// </summary>
public sealed record BatchHeader
{
    /// <summary>1-3: código do banco na compensação.</summary>
    [CnabField(1, 3, CnabFormat.Num)]
    public string Banco { get; set; } = string.Empty;

    /// <summary>4-7: lote de serviço.</summary>
    [CnabField(4, 4, CnabFormat.Num)]
    public int Lote { get; set; }

    /// <summary>8: tipo de registro, always 1.</summary>
    [CnabField(8, 1, CnabFormat.Const, Value = "1")]
    public string Registro { get; set; } = "1";

    /// <summary>9: tipo de operação (R remessa, T retorno).</summary>
    [CnabField(9, 1, CnabFormat.Alfa)]
    public string Operacao { get; set; } = string.Empty;

    /// <summary>10-11: tipo de serviço (01 cobrança).</summary>
    [CnabField(10, 2, CnabFormat.Num)]
    public int Servico { get; set; }

    /// <summary>12-13: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(12, 2, CnabFormat.Alfa)]
    public string CNAB1 { get; set; } = string.Empty;

    /// <summary>14-16: número da versão do layout do lote.</summary>
    [CnabField(14, 3, CnabFormat.Num)]
    public string VersaoLayout { get; set; } = string.Empty;

    /// <summary>17: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(17, 1, CnabFormat.Alfa)]
    public string CNAB2 { get; set; } = string.Empty;

    /// <summary>18: tipo de inscrição da empresa.</summary>
    [CnabField(18, 1, CnabFormat.Num)]
    public int TipoInscricao { get; set; }

    /// <summary>19-33: número de inscrição da empresa.</summary>
    [CnabField(19, 15, CnabFormat.Num)]
    public string NumeroInscricao { get; set; } = string.Empty;

    /// <summary>34-53: código do convênio no banco.</summary>
    [CnabField(34, 20, CnabFormat.Alfa)]
    public string Convenio { get; set; } = string.Empty;

    /// <summary>54-58: agência mantenedora da conta.</summary>
    [CnabField(54, 5, CnabFormat.Num)]
    public string Agencia { get; set; } = string.Empty;

    /// <summary>59: dígito verificador da agência.</summary>
    [CnabField(59, 1, CnabFormat.Alfa)]
    public string AgenciaDV { get; set; } = string.Empty;

    /// <summary>60-71: número da conta corrente.</summary>
    [CnabField(60, 12, CnabFormat.Num)]
    public string Conta { get; set; } = string.Empty;

    /// <summary>72: dígito verificador da conta.</summary>
    [CnabField(72, 1, CnabFormat.Alfa)]
    public string ContaDV { get; set; } = string.Empty;

    /// <summary>73: dígito verificador da agência/conta.</summary>
    [CnabField(73, 1, CnabFormat.Alfa)]
    public string AgenciaContaDV { get; set; } = string.Empty;

    /// <summary>74-103: nome da empresa.</summary>
    [CnabField(74, 30, CnabFormat.Alfa)]
    public string NomeEmpresa { get; set; } = string.Empty;

    /// <summary>104-143: mensagem 1.</summary>
    [CnabField(104, 40, CnabFormat.Alfa)]
    public string Mensagem1 { get; set; } = string.Empty;

    /// <summary>144-183: mensagem 2.</summary>
    [CnabField(144, 40, CnabFormat.Alfa)]
    public string Mensagem2 { get; set; } = string.Empty;

    /// <summary>184-191: número remessa/retorno.</summary>
    [CnabField(184, 8, CnabFormat.Num)]
    public int NumeroRemessa { get; set; }

    /// <summary>192-199: data de gravação remessa/retorno.</summary>
    [CnabField(192, 8, CnabFormat.Date)]
    public DateOnly? DataGravacao { get; set; }

    /// <summary>200-207: data do crédito.</summary>
    [CnabField(200, 8, CnabFormat.Date)]
    public DateOnly? DataCredito { get; set; }

    /// <summary>208-240: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(208, 33, CnabFormat.Alfa)]
    public string CNAB3 { get; set; } = string.Empty;
}

/// <summary>
/// The cobrança batch trailer, record type 5 ("trailer de lote").
/// <see cref="QuantidadeRegistros"/> counts every record of the batch including
/// its header and trailer. The per-carteira totals are normally filled by the
/// bank in retorno files and zero in remessa files; banks differ on whether
/// they refer to the file or to the whole portfolio.
/// </summary>
public sealed record BatchTrailer
{
    /// <summary>1-3: código do banco na compensação.</summary>
    [CnabField(1, 3, CnabFormat.Num)]
    public string Banco { get; set; } = string.Empty;

    /// <summary>4-7: lote de serviço.</summary>
    [CnabField(4, 4, CnabFormat.Num)]
    public int Lote { get; set; }

    /// <summary>8: tipo de registro, always 5.</summary>
    [CnabField(8, 1, CnabFormat.Const, Value = "5")]
    public string Registro { get; set; } = "5";

    /// <summary>9-17: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(9, 9, CnabFormat.Alfa)]
    public string CNAB1 { get; set; } = string.Empty;

    /// <summary>18-23: quantidade de registros do lote.</summary>
    [CnabField(18, 6, CnabFormat.Num)]
    public int QuantidadeRegistros { get; set; }

    /// <summary>24-29: quantidade de títulos em cobrança simples.</summary>
    [CnabField(24, 6, CnabFormat.Num)]
    public int QuantidadeSimples { get; set; }

    /// <summary>30-46: valor total dos títulos em carteiras simples (cents).</summary>
    [CnabField(30, 17, CnabFormat.Money)]
    public long ValorSimples { get; set; }

    /// <summary>47-52: quantidade de títulos em cobrança vinculada.</summary>
    [CnabField(47, 6, CnabFormat.Num)]
    public int QuantidadeVinculada { get; set; }

    /// <summary>53-69: valor total dos títulos em carteiras vinculadas (cents).</summary>
    [CnabField(53, 17, CnabFormat.Money)]
    public long ValorVinculada { get; set; }

    /// <summary>70-75: quantidade de títulos em cobrança caucionada.</summary>
    [CnabField(70, 6, CnabFormat.Num)]
    public int QuantidadeCaucionada { get; set; }

    /// <summary>76-92: valor total dos títulos em carteiras caucionadas (cents).</summary>
    [CnabField(76, 17, CnabFormat.Money)]
    public long ValorCaucionada { get; set; }

    /// <summary>93-98: quantidade de títulos em cobrança descontada.</summary>
    [CnabField(93, 6, CnabFormat.Num)]
    public int QuantidadeDescontada { get; set; }

    /// <summary>99-115: valor total dos títulos em carteiras descontadas (cents).</summary>
    [CnabField(99, 17, CnabFormat.Money)]
    public long ValorDescontada { get; set; }

    /// <summary>116-123: número do aviso de lançamento.</summary>
    [CnabField(116, 8, CnabFormat.Num)]
    public string NumeroAvisoLancamento { get; set; } = string.Empty;

    /// <summary>124-240: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(124, 117, CnabFormat.Alfa)]
    public string CNAB2 { get; set; } = string.Empty;
}

/// <summary>
/// The file trailer, record type 9 ("trailer de arquivo").
/// <see cref="QuantidadeRegistros"/> counts every line of the file, headers and
/// trailers included.
/// </summary>
public sealed record FileTrailer
{
    /// <summary>1-3: código do banco na compensação.</summary>
    [CnabField(1, 3, CnabFormat.Num)]
    public string Banco { get; set; } = string.Empty;

    /// <summary>4-7: lote de serviço (9999 in the file trailer).</summary>
    [CnabField(4, 4, CnabFormat.Num)]
    public int Lote { get; set; }

    /// <summary>8: tipo de registro, always 9.</summary>
    [CnabField(8, 1, CnabFormat.Const, Value = "9")]
    public string Registro { get; set; } = "9";

    /// <summary>9-17: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(9, 9, CnabFormat.Alfa)]
    public string CNAB1 { get; set; } = string.Empty;

    /// <summary>18-23: quantidade de lotes do arquivo.</summary>
    [CnabField(18, 6, CnabFormat.Num)]
    public int QuantidadeLotes { get; set; }

    /// <summary>24-29: quantidade de registros do arquivo.</summary>
    [CnabField(24, 6, CnabFormat.Num)]
    public int QuantidadeRegistros { get; set; }

    /// <summary>30-35: quantidade de contas para conciliação.</summary>
    [CnabField(30, 6, CnabFormat.Num)]
    public int QuantidadeContas { get; set; }

    /// <summary>36-240: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(36, 205, CnabFormat.Alfa)]
    public string CNAB2 { get; set; } = string.Empty;
}
