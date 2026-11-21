using Cnab.Internal;

namespace Cnab.Cnab240;

/// <summary>
/// A batch record: a typed detail segment (<see cref="SegmentP"/>,
/// <see cref="SegmentQ"/>, <see cref="SegmentR"/>, <see cref="SegmentT"/>,
/// <see cref="SegmentU"/>) or a <see cref="RawSegment"/> kept verbatim.
/// </summary>
public abstract record Segment
{
    /// <summary>The segment code at position 14 (<c>P</c>, <c>Q</c>, ...), or <c>'\0'</c> for raw records that are not type 3.</summary>
    public abstract char Code { get; }

    /// <summary>Encodes the segment as a 240-character line.</summary>
    /// <returns>The encoded line.</returns>
    /// <exception cref="CnabFieldException">A value cannot be represented in its field.</exception>
    /// <exception cref="CnabException">A raw segment is not 240 characters long (<see cref="CnabError.RawLength"/>).</exception>
    public abstract string Encode();

    /// <summary>
    /// Decodes a 240-character batch record (type 2, 3 or 4). Type 3 records
    /// with segment codes P, Q, R, T or U decode to their record; any other
    /// record is returned as a <see cref="RawSegment"/>.
    /// </summary>
    /// <param name="line">The line, without terminator.</param>
    /// <returns>The decoded segment.</returns>
    /// <exception cref="CnabFieldException">The line is not 240 characters long or a field is invalid.</exception>
    public static Segment Decode(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Length != Layout240.LineLength)
        {
            throw new CnabFieldException(
                CnabError.LineLength,
                "Segment",
                1,
                Layout240.LineLength,
                string.Empty,
                "line has " + GoText.Str(line.Length) + " bytes, want 240",
                valueFromFile: true);
        }

        if (line[7] != RecordType.Detalhe)
        {
            return new RawSegment(line);
        }

        return line[13] switch
        {
            'P' => FixedWidth.Deserialize<SegmentP>(line),
            'Q' => FixedWidth.Deserialize<SegmentQ>(line),
            'R' => FixedWidth.Deserialize<SegmentR>(line),
            'T' => FixedWidth.Deserialize<SegmentT>(line),
            'U' => FixedWidth.Deserialize<SegmentU>(line),
            _ => new RawSegment(line),
        };
    }
}

/// <summary>
/// Positions 1-13 shared by every detail segment: control fields, record type
/// 3 and the record's sequence number within the batch.
/// </summary>
public abstract record DetailSegment : Segment
{
    /// <summary>1-3: código do banco na compensação.</summary>
    [CnabField(1, 3, CnabFormat.Num)]
    public string Banco { get; set; } = string.Empty;

    /// <summary>4-7: lote de serviço.</summary>
    [CnabField(4, 4, CnabFormat.Num)]
    public int Lote { get; set; }

    /// <summary>8: tipo de registro, always 3.</summary>
    [CnabField(8, 1, CnabFormat.Const, Value = "3")]
    public string Registro { get; set; } = "3";

    /// <summary>9-13: número sequencial do registro no lote.</summary>
    [CnabField(9, 5, CnabFormat.Num)]
    public int Sequencial { get; set; }
}

/// <summary>
/// Segment P: the title data of a remessa (mandatory).
/// </summary>
/// <remarks>
/// <see cref="NossoNumero"/> (38-57) is where banks diverge the most: each bank
/// defines its own sub-layout and alignment (left-aligned with spaces or
/// right-aligned with zeros, with or without check digit). It is kept as text.
/// </remarks>
public sealed record SegmentP : DetailSegment
{
    /// <inheritdoc/>
    public override char Code => 'P';

    /// <summary>14: código do segmento, always P.</summary>
    [CnabField(14, 1, CnabFormat.Const, Value = "P")]
    public string Segmento { get; set; } = "P";

    /// <summary>15: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(15, 1, CnabFormat.Alfa)]
    public string CNAB1 { get; set; } = string.Empty;

    /// <summary>16-17: código de movimento remessa.</summary>
    [CnabField(16, 2, CnabFormat.Num)]
    public MovimentoRemessa Movimento { get; set; }

    /// <summary>18-22: agência mantenedora da conta.</summary>
    [CnabField(18, 5, CnabFormat.Num)]
    public string Agencia { get; set; } = string.Empty;

    /// <summary>23: dígito verificador da agência.</summary>
    [CnabField(23, 1, CnabFormat.Alfa)]
    public string AgenciaDV { get; set; } = string.Empty;

    /// <summary>24-35: número da conta corrente.</summary>
    [CnabField(24, 12, CnabFormat.Num)]
    public string Conta { get; set; } = string.Empty;

    /// <summary>36: dígito verificador da conta.</summary>
    [CnabField(36, 1, CnabFormat.Alfa)]
    public string ContaDV { get; set; } = string.Empty;

    /// <summary>37: dígito verificador da agência/conta.</summary>
    [CnabField(37, 1, CnabFormat.Alfa)]
    public string AgenciaContaDV { get; set; } = string.Empty;

    /// <summary>38-57: identificação do título no banco (nosso número).</summary>
    [CnabField(38, 20, CnabFormat.Alfa)]
    public string NossoNumero { get; set; } = string.Empty;

    /// <summary>58: código da carteira.</summary>
    [CnabField(58, 1, CnabFormat.Num)]
    public int Carteira { get; set; }

    /// <summary>59: forma de cadastramento do título no banco.</summary>
    [CnabField(59, 1, CnabFormat.Num)]
    public int FormaCadastramento { get; set; }

    /// <summary>60: tipo de documento.</summary>
    [CnabField(60, 1, CnabFormat.Alfa)]
    public string TipoDocumento { get; set; } = string.Empty;

    /// <summary>61: identificação da emissão do boleto.</summary>
    [CnabField(61, 1, CnabFormat.Num)]
    public int EmissaoBoleto { get; set; }

    /// <summary>62: identificação da distribuição do boleto.</summary>
    [CnabField(62, 1, CnabFormat.Alfa)]
    public string DistribuicaoBoleto { get; set; } = string.Empty;

    /// <summary>63-77: número do documento de cobrança ("seu número").</summary>
    [CnabField(63, 15, CnabFormat.Alfa)]
    public string NumeroDocumento { get; set; } = string.Empty;

    /// <summary>78-85: data de vencimento do título.</summary>
    [CnabField(78, 8, CnabFormat.Date)]
    public DateOnly? Vencimento { get; set; }

    /// <summary>86-100: valor nominal do título (cents).</summary>
    [CnabField(86, 15, CnabFormat.Money)]
    public long Valor { get; set; }

    /// <summary>101-105: agência encarregada da cobrança.</summary>
    [CnabField(101, 5, CnabFormat.Num)]
    public string AgenciaCobradora { get; set; } = string.Empty;

    /// <summary>106: dígito verificador da agência cobradora.</summary>
    [CnabField(106, 1, CnabFormat.Alfa)]
    public string AgenciaCobradoraDV { get; set; } = string.Empty;

    /// <summary>107-108: espécie do título.</summary>
    [CnabField(107, 2, CnabFormat.Num)]
    public Especie Especie { get; set; }

    /// <summary>109: identificação de título aceito (A) / não aceito (N).</summary>
    [CnabField(109, 1, CnabFormat.Alfa)]
    public string Aceite { get; set; } = string.Empty;

    /// <summary>110-117: data da emissão do título.</summary>
    [CnabField(110, 8, CnabFormat.Date)]
    public DateOnly? DataEmissao { get; set; }

    /// <summary>118: código do juros de mora.</summary>
    [CnabField(118, 1, CnabFormat.Num)]
    public int CodigoJuros { get; set; }

    /// <summary>119-126: data do juros de mora.</summary>
    [CnabField(119, 8, CnabFormat.Date)]
    public DateOnly? DataJuros { get; set; }

    /// <summary>127-141: juros de mora por dia (value) or taxa, per <see cref="CodigoJuros"/>.</summary>
    [CnabField(127, 15, CnabFormat.Money)]
    public long Juros { get; set; }

    /// <summary>142: código do desconto 1.</summary>
    [CnabField(142, 1, CnabFormat.Num)]
    public int CodigoDesconto { get; set; }

    /// <summary>143-150: data do desconto 1.</summary>
    [CnabField(143, 8, CnabFormat.Date)]
    public DateOnly? DataDesconto { get; set; }

    /// <summary>151-165: valor/percentual a ser concedido (desconto 1).</summary>
    [CnabField(151, 15, CnabFormat.Money)]
    public long Desconto { get; set; }

    /// <summary>166-180: valor do IOF a ser recolhido.</summary>
    [CnabField(166, 15, CnabFormat.Money)]
    public long IOF { get; set; }

    /// <summary>181-195: valor do abatimento.</summary>
    [CnabField(181, 15, CnabFormat.Money)]
    public long Abatimento { get; set; }

    /// <summary>196-220: identificação do título na empresa.</summary>
    [CnabField(196, 25, CnabFormat.Alfa)]
    public string UsoEmpresa { get; set; } = string.Empty;

    /// <summary>221: código para protesto.</summary>
    [CnabField(221, 1, CnabFormat.Num)]
    public int CodigoProtesto { get; set; }

    /// <summary>222-223: número de dias para protesto.</summary>
    [CnabField(222, 2, CnabFormat.Num)]
    public int DiasProtesto { get; set; }

    /// <summary>224: código para baixa/devolução.</summary>
    [CnabField(224, 1, CnabFormat.Num)]
    public int CodigoBaixa { get; set; }

    /// <summary>225-227: número de dias para baixa/devolução (numeric in the manual, often blank).</summary>
    [CnabField(225, 3, CnabFormat.Alfa)]
    public string DiasBaixa { get; set; } = string.Empty;

    /// <summary>228-229: código da moeda.</summary>
    [CnabField(228, 2, CnabFormat.Num)]
    public int Moeda { get; set; }

    /// <summary>230-239: número do contrato da operação de crédito.</summary>
    [CnabField(230, 10, CnabFormat.Num)]
    public string NumeroContrato { get; set; } = string.Empty;

    /// <summary>240: uso livre banco/empresa.</summary>
    [CnabField(240, 1, CnabFormat.Alfa)]
    public string UsoLivre { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override string Encode() => FixedWidth.Serialize(this, Layout240.LineLength);
}

/// <summary>
/// Segment Q: payer ("pagador") and guarantor ("sacador/avalista") data of a
/// remessa (mandatory after P).
/// </summary>
public sealed record SegmentQ : DetailSegment
{
    /// <inheritdoc/>
    public override char Code => 'Q';

    /// <summary>14: código do segmento, always Q.</summary>
    [CnabField(14, 1, CnabFormat.Const, Value = "Q")]
    public string Segmento { get; set; } = "Q";

    /// <summary>15: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(15, 1, CnabFormat.Alfa)]
    public string CNAB1 { get; set; } = string.Empty;

    /// <summary>16-17: código de movimento remessa.</summary>
    [CnabField(16, 2, CnabFormat.Num)]
    public MovimentoRemessa Movimento { get; set; }

    /// <summary>18: tipo de inscrição do pagador.</summary>
    [CnabField(18, 1, CnabFormat.Num)]
    public int TipoInscricao { get; set; }

    /// <summary>19-33: número de inscrição do pagador.</summary>
    [CnabField(19, 15, CnabFormat.Num)]
    public string NumeroInscricao { get; set; } = string.Empty;

    /// <summary>34-73: nome do pagador.</summary>
    [CnabField(34, 40, CnabFormat.Alfa)]
    public string Nome { get; set; } = string.Empty;

    /// <summary>74-113: endereço do pagador.</summary>
    [CnabField(74, 40, CnabFormat.Alfa)]
    public string Endereco { get; set; } = string.Empty;

    /// <summary>114-128: bairro do pagador.</summary>
    [CnabField(114, 15, CnabFormat.Alfa)]
    public string Bairro { get; set; } = string.Empty;

    /// <summary>129-133: CEP do pagador.</summary>
    [CnabField(129, 5, CnabFormat.Num)]
    public string CEP { get; set; } = string.Empty;

    /// <summary>134-136: sufixo do CEP do pagador.</summary>
    [CnabField(134, 3, CnabFormat.Num)]
    public string SufixoCEP { get; set; } = string.Empty;

    /// <summary>137-151: cidade do pagador.</summary>
    [CnabField(137, 15, CnabFormat.Alfa)]
    public string Cidade { get; set; } = string.Empty;

    /// <summary>152-153: unidade da federação do pagador.</summary>
    [CnabField(152, 2, CnabFormat.Alfa)]
    public string UF { get; set; } = string.Empty;

    /// <summary>154: tipo de inscrição do sacador/avalista.</summary>
    [CnabField(154, 1, CnabFormat.Num)]
    public int TipoInscricaoAvalista { get; set; }

    /// <summary>155-169: número de inscrição do sacador/avalista.</summary>
    [CnabField(155, 15, CnabFormat.Num)]
    public string InscricaoAvalista { get; set; } = string.Empty;

    /// <summary>170-209: nome do sacador/avalista.</summary>
    [CnabField(170, 40, CnabFormat.Alfa)]
    public string NomeAvalista { get; set; } = string.Empty;

    /// <summary>210-212: código do banco correspondente na compensação.</summary>
    [CnabField(210, 3, CnabFormat.Num)]
    public string BancoCorrespondente { get; set; } = string.Empty;

    /// <summary>213-232: nosso número no banco correspondente.</summary>
    [CnabField(213, 20, CnabFormat.Alfa)]
    public string NossoNumeroCorresp { get; set; } = string.Empty;

    /// <summary>233-240: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(233, 8, CnabFormat.Alfa)]
    public string CNAB2 { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override string Encode() => FixedWidth.Serialize(this, Layout240.LineLength);
}

/// <summary>
/// Segment R: optional remessa data: second and third discounts, fine
/// ("multa"), messages and automatic debit data.
/// </summary>
public sealed record SegmentR : DetailSegment
{
    /// <inheritdoc/>
    public override char Code => 'R';

    /// <summary>14: código do segmento, always R.</summary>
    [CnabField(14, 1, CnabFormat.Const, Value = "R")]
    public string Segmento { get; set; } = "R";

    /// <summary>15: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(15, 1, CnabFormat.Alfa)]
    public string CNAB1 { get; set; } = string.Empty;

    /// <summary>16-17: código de movimento remessa.</summary>
    [CnabField(16, 2, CnabFormat.Num)]
    public MovimentoRemessa Movimento { get; set; }

    /// <summary>18: código do desconto 2.</summary>
    [CnabField(18, 1, CnabFormat.Num)]
    public int CodigoDesconto2 { get; set; }

    /// <summary>19-26: data do desconto 2.</summary>
    [CnabField(19, 8, CnabFormat.Date)]
    public DateOnly? DataDesconto2 { get; set; }

    /// <summary>27-41: valor/percentual a ser concedido (desconto 2).</summary>
    [CnabField(27, 15, CnabFormat.Money)]
    public long Desconto2 { get; set; }

    /// <summary>42: código do desconto 3.</summary>
    [CnabField(42, 1, CnabFormat.Num)]
    public int CodigoDesconto3 { get; set; }

    /// <summary>43-50: data do desconto 3.</summary>
    [CnabField(43, 8, CnabFormat.Date)]
    public DateOnly? DataDesconto3 { get; set; }

    /// <summary>51-65: valor/percentual a ser concedido (desconto 3).</summary>
    [CnabField(51, 15, CnabFormat.Money)]
    public long Desconto3 { get; set; }

    /// <summary>66: código da multa.</summary>
    [CnabField(66, 1, CnabFormat.Num)]
    public int CodigoMulta { get; set; }

    /// <summary>67-74: data da multa.</summary>
    [CnabField(67, 8, CnabFormat.Date)]
    public DateOnly? DataMulta { get; set; }

    /// <summary>75-89: valor/percentual a ser aplicado (multa), per <see cref="CodigoMulta"/>.</summary>
    [CnabField(75, 15, CnabFormat.Money)]
    public long Multa { get; set; }

    /// <summary>90-99: informação ao pagador.</summary>
    [CnabField(90, 10, CnabFormat.Alfa)]
    public string InformacaoPagador { get; set; } = string.Empty;

    /// <summary>100-139: mensagem 3.</summary>
    [CnabField(100, 40, CnabFormat.Alfa)]
    public string Mensagem3 { get; set; } = string.Empty;

    /// <summary>140-179: mensagem 4.</summary>
    [CnabField(140, 40, CnabFormat.Alfa)]
    public string Mensagem4 { get; set; } = string.Empty;

    /// <summary>180-199: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(180, 20, CnabFormat.Alfa)]
    public string CNAB2 { get; set; } = string.Empty;

    /// <summary>200-207: código de ocorrência do pagador.</summary>
    [CnabField(200, 8, CnabFormat.Alfa)]
    public string OcorrenciaPagador { get; set; } = string.Empty;

    /// <summary>208-210: código do banco na conta do débito.</summary>
    [CnabField(208, 3, CnabFormat.Num)]
    public string BancoDebito { get; set; } = string.Empty;

    /// <summary>211-215: código da agência do débito.</summary>
    [CnabField(211, 5, CnabFormat.Num)]
    public string AgenciaDebito { get; set; } = string.Empty;

    /// <summary>216: dígito verificador da agência do débito.</summary>
    [CnabField(216, 1, CnabFormat.Alfa)]
    public string AgenciaDebitoDV { get; set; } = string.Empty;

    /// <summary>217-228: conta corrente para débito.</summary>
    [CnabField(217, 12, CnabFormat.Num)]
    public string ContaDebito { get; set; } = string.Empty;

    /// <summary>229: dígito verificador da conta do débito.</summary>
    [CnabField(229, 1, CnabFormat.Alfa)]
    public string ContaDebitoDV { get; set; } = string.Empty;

    /// <summary>230: dígito verificador da agência/conta do débito.</summary>
    [CnabField(230, 1, CnabFormat.Alfa)]
    public string AgContaDebitoDV { get; set; } = string.Empty;

    /// <summary>231: aviso para débito automático.</summary>
    [CnabField(231, 1, CnabFormat.Alfa)]
    public string AvisoDebito { get; set; } = string.Empty;

    /// <summary>232-240: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(232, 9, CnabFormat.Alfa)]
    public string CNAB3 { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override string Encode() => FixedWidth.Serialize(this, Layout240.LineLength);
}

/// <summary>
/// Segment T: the title data of a retorno.
/// </summary>
/// <remarks>
/// <see cref="Motivos"/> (214-223) holds up to five two-character reason codes
/// whose meaning depends on <see cref="Movimento"/> and, in practice, on the
/// bank. Use <see cref="MotivoCodes"/> to split them.
/// </remarks>
public sealed record SegmentT : DetailSegment
{
    /// <inheritdoc/>
    public override char Code => 'T';

    /// <summary>14: código do segmento, always T.</summary>
    [CnabField(14, 1, CnabFormat.Const, Value = "T")]
    public string Segmento { get; set; } = "T";

    /// <summary>15: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(15, 1, CnabFormat.Alfa)]
    public string CNAB1 { get; set; } = string.Empty;

    /// <summary>16-17: código de movimento retorno.</summary>
    [CnabField(16, 2, CnabFormat.Num)]
    public MovimentoRetorno Movimento { get; set; }

    /// <summary>18-22: agência mantenedora da conta.</summary>
    [CnabField(18, 5, CnabFormat.Num)]
    public string Agencia { get; set; } = string.Empty;

    /// <summary>23: dígito verificador da agência.</summary>
    [CnabField(23, 1, CnabFormat.Alfa)]
    public string AgenciaDV { get; set; } = string.Empty;

    /// <summary>24-35: número da conta corrente.</summary>
    [CnabField(24, 12, CnabFormat.Num)]
    public string Conta { get; set; } = string.Empty;

    /// <summary>36: dígito verificador da conta.</summary>
    [CnabField(36, 1, CnabFormat.Alfa)]
    public string ContaDV { get; set; } = string.Empty;

    /// <summary>37: dígito verificador da agência/conta.</summary>
    [CnabField(37, 1, CnabFormat.Alfa)]
    public string AgenciaContaDV { get; set; } = string.Empty;

    /// <summary>38-57: identificação do título no banco (nosso número).</summary>
    [CnabField(38, 20, CnabFormat.Alfa)]
    public string NossoNumero { get; set; } = string.Empty;

    /// <summary>58: código da carteira.</summary>
    [CnabField(58, 1, CnabFormat.Num)]
    public int Carteira { get; set; }

    /// <summary>59-73: número do documento de cobrança.</summary>
    [CnabField(59, 15, CnabFormat.Alfa)]
    public string NumeroDocumento { get; set; } = string.Empty;

    /// <summary>74-81: data do vencimento do título.</summary>
    [CnabField(74, 8, CnabFormat.Date)]
    public DateOnly? Vencimento { get; set; }

    /// <summary>82-96: valor nominal do título (cents).</summary>
    [CnabField(82, 15, CnabFormat.Money)]
    public long Valor { get; set; }

    /// <summary>97-99: número do banco cobrador/recebedor.</summary>
    [CnabField(97, 3, CnabFormat.Num)]
    public string BancoCobrador { get; set; } = string.Empty;

    /// <summary>100-104: agência cobradora/recebedora.</summary>
    [CnabField(100, 5, CnabFormat.Num)]
    public string AgenciaCobradora { get; set; } = string.Empty;

    /// <summary>105: dígito verificador da agência cobradora.</summary>
    [CnabField(105, 1, CnabFormat.Alfa)]
    public string AgenciaCobradoraDV { get; set; } = string.Empty;

    /// <summary>106-130: identificação do título na empresa.</summary>
    [CnabField(106, 25, CnabFormat.Alfa)]
    public string UsoEmpresa { get; set; } = string.Empty;

    /// <summary>131-132: código da moeda.</summary>
    [CnabField(131, 2, CnabFormat.Num)]
    public int Moeda { get; set; }

    /// <summary>133: tipo de inscrição do pagador.</summary>
    [CnabField(133, 1, CnabFormat.Num)]
    public int TipoInscricao { get; set; }

    /// <summary>134-148: número de inscrição do pagador.</summary>
    [CnabField(134, 15, CnabFormat.Num)]
    public string NumeroInscricao { get; set; } = string.Empty;

    /// <summary>149-188: nome do pagador.</summary>
    [CnabField(149, 40, CnabFormat.Alfa)]
    public string Nome { get; set; } = string.Empty;

    /// <summary>189-198: número do contrato da operação de crédito.</summary>
    [CnabField(189, 10, CnabFormat.Num)]
    public string NumeroContrato { get; set; } = string.Empty;

    /// <summary>199-213: valor da tarifa / custas (cents).</summary>
    [CnabField(199, 15, CnabFormat.Money)]
    public long Tarifa { get; set; }

    /// <summary>214-223: identificação para rejeições, tarifas, custas, liquidação e baixas (motivos).</summary>
    [CnabField(214, 10, CnabFormat.Alfa)]
    public string Motivos { get; set; } = string.Empty;

    /// <summary>224-240: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(224, 17, CnabFormat.Alfa)]
    public string CNAB2 { get; set; } = string.Empty;

    /// <summary>Splits <see cref="Motivos"/> into its non-blank, non-<c>00</c> two-character codes.</summary>
    /// <returns>The reason codes, possibly empty.</returns>
    public IReadOnlyList<string> MotivoCodes()
    {
        var codes = new List<string>();
        string m = Motivos;
        for (int i = 0; i + 2 <= m.Length; i += 2)
        {
            string c = m.Substring(i, 2);
            if (c != "  " && c != "00")
            {
                codes.Add(c);
            }
        }

        if (m.Length % 2 == 1 && m[^1] != ' ')
        {
            codes.Add(m.Substring(m.Length - 1));
        }

        return codes;
    }

    /// <inheritdoc/>
    public override string Encode() => FixedWidth.Serialize(this, Layout240.LineLength);
}

/// <summary>
/// Segment U: the amounts and dates of a retorno occurrence (follows T).
/// </summary>
/// <remarks>
/// <see cref="ValorPago"/> is what the payer paid; <see cref="ValorLiquido"/>
/// is what will be credited (banks differ on whether tariffs are already deducted).
/// </remarks>
public sealed record SegmentU : DetailSegment
{
    /// <inheritdoc/>
    public override char Code => 'U';

    /// <summary>14: código do segmento, always U.</summary>
    [CnabField(14, 1, CnabFormat.Const, Value = "U")]
    public string Segmento { get; set; } = "U";

    /// <summary>15: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(15, 1, CnabFormat.Alfa)]
    public string CNAB1 { get; set; } = string.Empty;

    /// <summary>16-17: código de movimento retorno.</summary>
    [CnabField(16, 2, CnabFormat.Num)]
    public MovimentoRetorno Movimento { get; set; }

    /// <summary>18-32: juros, multa e encargos (cents).</summary>
    [CnabField(18, 15, CnabFormat.Money)]
    public long JurosMulta { get; set; }

    /// <summary>33-47: valor do desconto concedido (cents).</summary>
    [CnabField(33, 15, CnabFormat.Money)]
    public long Desconto { get; set; }

    /// <summary>48-62: valor do abatimento concedido/cancelado (cents).</summary>
    [CnabField(48, 15, CnabFormat.Money)]
    public long Abatimento { get; set; }

    /// <summary>63-77: valor do IOF recolhido (cents).</summary>
    [CnabField(63, 15, CnabFormat.Money)]
    public long IOF { get; set; }

    /// <summary>78-92: valor pago pelo pagador (cents).</summary>
    [CnabField(78, 15, CnabFormat.Money)]
    public long ValorPago { get; set; }

    /// <summary>93-107: valor líquido a ser creditado (cents).</summary>
    [CnabField(93, 15, CnabFormat.Money)]
    public long ValorLiquido { get; set; }

    /// <summary>108-122: valor de outras despesas (cents).</summary>
    [CnabField(108, 15, CnabFormat.Money)]
    public long OutrasDespesas { get; set; }

    /// <summary>123-137: valor de outros créditos (cents).</summary>
    [CnabField(123, 15, CnabFormat.Money)]
    public long OutrosCreditos { get; set; }

    /// <summary>138-145: data da ocorrência.</summary>
    [CnabField(138, 8, CnabFormat.Date)]
    public DateOnly? DataOcorrencia { get; set; }

    /// <summary>146-153: data da efetivação do crédito.</summary>
    [CnabField(146, 8, CnabFormat.Date)]
    public DateOnly? DataCredito { get; set; }

    /// <summary>154-157: código da ocorrência do pagador.</summary>
    [CnabField(154, 4, CnabFormat.Alfa)]
    public string OcorrenciaPagador { get; set; } = string.Empty;

    /// <summary>158-165: data da ocorrência do pagador.</summary>
    [CnabField(158, 8, CnabFormat.Date)]
    public DateOnly? DataOcorrenciaPagador { get; set; }

    /// <summary>166-180: valor da ocorrência do pagador (cents).</summary>
    [CnabField(166, 15, CnabFormat.Money)]
    public long ValorOcorrenciaPagador { get; set; }

    /// <summary>181-210: complemento da ocorrência do pagador.</summary>
    [CnabField(181, 30, CnabFormat.Alfa)]
    public string ComplementoOcorrencia { get; set; } = string.Empty;

    /// <summary>211-213: código do banco correspondente na compensação.</summary>
    [CnabField(211, 3, CnabFormat.Num)]
    public string BancoCorrespondente { get; set; } = string.Empty;

    /// <summary>214-233: nosso número no banco correspondente.</summary>
    [CnabField(214, 20, CnabFormat.Alfa)]
    public string NossoNumeroCorresp { get; set; } = string.Empty;

    /// <summary>234-240: uso exclusivo FEBRABAN/CNAB.</summary>
    [CnabField(234, 7, CnabFormat.Alfa)]
    public string CNAB2 { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override string Encode() => FixedWidth.Serialize(this, Layout240.LineLength);
}

/// <summary>
/// A batch record with no typed counterpart (segments such as A, J, S, Y, or
/// record types 2 and 4), kept verbatim so files round-trip losslessly.
/// </summary>
/// <param name="Line">The record, without terminator.</param>
public sealed record RawSegment(string Line) : Segment
{
    /// <summary>The character at position 14 for type 3 records, <c>'\0'</c> otherwise.</summary>
    public override char Code => Line.Length >= 14 && Line[7] == RecordType.Detalhe ? Line[13] : (char)0;

    /// <summary>Returns <see cref="Line"/>, which must be 240 characters long.</summary>
    /// <returns>The line.</returns>
    /// <exception cref="CnabException"><see cref="Line"/> is not 240 characters long (<see cref="CnabError.RawLength"/>).</exception>
    public override string Encode()
    {
        if (Line.Length != Layout240.LineLength)
        {
            throw new CnabException(
                CnabError.RawLength,
                CnabException.Describe(CnabError.RawLength) + ", have " + GoText.Str(Line.Length));
        }

        return Line;
    }
}
