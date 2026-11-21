using System.Diagnostics.CodeAnalysis;

namespace Cnab.Cnab400;

/// <summary>Constants of the CNAB 400 layout.</summary>
public static class Layout400
{
    /// <summary>The length in characters of every CNAB 400 record.</summary>
    public const int LineLength = 400;
}

/// <summary>
/// The generic cobrança file header (record type 0). Positions 27-46
/// (company code) and 101-394 are bank-specific and kept raw.
/// </summary>
/// <remarks>
/// Unlike CNAB 240, the 400 layouts were never standardized in practice:
/// every bank defines positions 2-394 of the detail record itself. Only the
/// parts common to the mainstream cobrança layouts are typed here.
/// </remarks>
public sealed record Header
{
    /// <summary>1: tipo de registro, always 0.</summary>
    [CnabField(1, 1, CnabFormat.Const, Value = "0")]
    public string Registro { get; set; } = "0";

    /// <summary>2: operação (1 remessa, 2 retorno).</summary>
    [CnabField(2, 1, CnabFormat.Num)]
    public int Operacao { get; set; }

    /// <summary>3-9: literal da operação (<c>REMESSA</c> / <c>RETORNO</c>).</summary>
    [CnabField(3, 7, CnabFormat.Alfa)]
    public string LiteralOperacao { get; set; } = string.Empty;

    /// <summary>10-11: código do serviço (01 cobrança).</summary>
    [CnabField(10, 2, CnabFormat.Num)]
    public int Servico { get; set; }

    /// <summary>12-26: literal do serviço (<c>COBRANCA</c>).</summary>
    [CnabField(12, 15, CnabFormat.Alfa)]
    public string LiteralServico { get; set; } = string.Empty;

    /// <summary>27-46: código da empresa, bank-specific.</summary>
    [CnabField(27, 20, CnabFormat.Raw)]
    public string CodigoEmpresa { get; set; } = string.Empty;

    /// <summary>47-76: nome da empresa.</summary>
    [CnabField(47, 30, CnabFormat.Alfa)]
    public string NomeEmpresa { get; set; } = string.Empty;

    /// <summary>77-79: código do banco na compensação.</summary>
    [CnabField(77, 3, CnabFormat.Num)]
    public string Banco { get; set; } = string.Empty;

    /// <summary>80-94: nome do banco.</summary>
    [CnabField(80, 15, CnabFormat.Alfa)]
    public string NomeBanco { get; set; } = string.Empty;

    /// <summary>95-100: data de gravação (DDMMYY).</summary>
    [CnabField(95, 6, CnabFormat.Date)]
    public DateOnly? DataGravacao { get; set; }

    /// <summary>101-394: bank-specific area, kept verbatim.</summary>
    [CnabField(101, 294, CnabFormat.Raw)]
    public string Reservado { get; set; } = string.Empty;

    /// <summary>395-400: número sequencial do registro.</summary>
    [CnabField(395, 6, CnabFormat.Num)]
    public int Sequencial { get; set; }
}

/// <summary>
/// A detail record with only the record type and sequence number typed. Use
/// <see cref="Decode{T}"/> to read it with a bank-specific record.
/// </summary>
public sealed record Detail
{
    /// <summary>1: tipo de registro (1 for cobrança details; banks add 2, 5, 7, ...).</summary>
    [CnabField(1, 1, CnabFormat.Raw)]
    public string Registro { get; set; } = string.Empty;

    /// <summary>2-394: the bank-specific content, kept verbatim.</summary>
    [CnabField(2, 393, CnabFormat.Raw)]
    public string Dados { get; set; } = string.Empty;

    /// <summary>395-400: número sequencial do registro.</summary>
    [CnabField(395, 6, CnabFormat.Num)]
    public int Sequencial { get; set; }

    /// <summary>Re-encodes the detail as a 400-character line.</summary>
    /// <returns>The line.</returns>
    /// <exception cref="CnabFieldException">A value cannot be represented in its field.</exception>
    public string ToLine() => FixedWidth.Serialize(this, Layout400.LineLength);

    /// <summary>Decodes the detail with a bank-specific record whose layout covers positions 1-400.</summary>
    /// <typeparam name="T">The bank-specific record type.</typeparam>
    /// <returns>The decoded record.</returns>
    /// <exception cref="CnabFieldException">The detail does not match the layout.</exception>
    /// <exception cref="CnabLayoutException">The layout of <typeparamref name="T"/> is invalid.</exception>
    public T Decode<[DynamicallyAccessedMembers(FixedWidth.RecordMembers)] T>()
        where T : class, new() => FixedWidth.Deserialize<T>(ToLine());
}

/// <summary>
/// The file trailer (record type 9). Positions 2-394 are bank-specific
/// (retorno trailers usually carry totals) and kept raw.
/// </summary>
public sealed record Trailer
{
    /// <summary>1: tipo de registro, always 9.</summary>
    [CnabField(1, 1, CnabFormat.Const, Value = "9")]
    public string Registro { get; set; } = "9";

    /// <summary>2-394: bank-specific area, kept verbatim.</summary>
    [CnabField(2, 393, CnabFormat.Raw)]
    public string Reservado { get; set; } = string.Empty;

    /// <summary>395-400: número sequencial do registro.</summary>
    [CnabField(395, 6, CnabFormat.Num)]
    public int Sequencial { get; set; }
}

/// <summary>A parsed CNAB 400 file.</summary>
public sealed class Cnab400File
{
    /// <summary>The header (record type 0).</summary>
    public Header Header { get; set; } = new();

    /// <summary>The detail records in file order.</summary>
    public List<Detail> Details { get; set; } = new();

    /// <summary>The trailer (record type 9).</summary>
    public Trailer Trailer { get; set; } = new();
}
