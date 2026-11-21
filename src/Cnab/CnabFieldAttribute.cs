namespace Cnab;

/// <summary>How a field is encoded in a fixed-width record.</summary>
public enum CnabFormat
{
    /// <summary>
    /// Digits, right-aligned and zero-padded. The property is a
    /// <see cref="string"/> (kept verbatim, so leading zeros of agência or
    /// CNPJ survive) or an integer type (<see cref="byte"/>, <see cref="short"/>,
    /// <see cref="int"/>, <see cref="long"/> or an enum backed by one of them).
    /// An all-blank field decodes as an empty string or zero.
    /// </summary>
    Num = 1,

    /// <summary>
    /// Left-aligned, space-padded text. On encode the value is upper-cased and
    /// accents are folded to ASCII (<c>São João</c> becomes <c>SAO JOAO</c>);
    /// any other non-ASCII or control character is an error. On decode trailing
    /// spaces are trimmed.
    /// </summary>
    Alfa,

    /// <summary>
    /// Like <see cref="Alfa"/> but without case folding or trimming, for
    /// bank-reserved areas, so round trips are lossless.
    /// </summary>
    Raw,

    /// <summary>
    /// A <see cref="DateOnly"/>? property. Width 8 is <c>DDMMYYYY</c>, width 6 is
    /// <c>DDMMYY</c> (years 69-99 map to 19xx, 00-68 to 20xx). All zeros or all
    /// blanks mean "no date" and decode to <see langword="null"/>.
    /// </summary>
    Date,

    /// <summary>
    /// An integer property holding cents. The field carries two implied decimal
    /// places, so <c>000000000012345</c> is 12345 cents (R$ 123,45). Negative
    /// values are rejected: CNAB has no sign.
    /// </summary>
    Money,

    /// <summary>
    /// A string property whose content must equal
    /// <see cref="CnabFieldAttribute.Value"/>. Encode writes the literal when
    /// the property is empty; decode fails if the line holds anything else.
    /// </summary>
    Const,
}

/// <summary>
/// Declares the position and format of a property in a fixed-width record,
/// exactly as printed in bank manuals: 1-based start and length.
/// </summary>
/// <example>
/// <code>
/// public sealed record Pagamento
/// {
///     [CnabField(1, 1, CnabFormat.Const, Value = "1")] public string Tipo { get; set; } = "1";
///     [CnabField(2, 15, CnabFormat.Alfa)] public string Nome { get; set; } = "";
///     [CnabField(17, 8, CnabFormat.Date)] public DateOnly? Data { get; set; }
///     [CnabField(25, 10, CnabFormat.Money)] public long Valor { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CnabFieldAttribute : Attribute
{
    /// <summary>Declares a field.</summary>
    /// <param name="start">The first position, 1-based.</param>
    /// <param name="length">The number of positions.</param>
    /// <param name="format">The encoding.</param>
    public CnabFieldAttribute(int start, int length, CnabFormat format)
    {
        Start = start;
        Length = length;
        Format = format;
    }

    /// <summary>The first position, 1-based.</summary>
    public int Start { get; }

    /// <summary>The number of positions.</summary>
    public int Length { get; }

    /// <summary>The last position, 1-based and inclusive.</summary>
    public int End => Start + Length - 1;

    /// <summary>The encoding.</summary>
    public CnabFormat Format { get; }

    /// <summary>The literal of a <see cref="CnabFormat.Const"/> field; its length must equal <see cref="Length"/>.</summary>
    public string? Value { get; set; }
}

/// <summary>One field of a compiled layout, for documentation and tooling.</summary>
/// <param name="Name">The property name.</param>
/// <param name="Start">The first position, 1-based.</param>
/// <param name="End">The last position, 1-based and inclusive.</param>
/// <param name="Format">The encoding.</param>
/// <param name="Const">The literal of a constant field, otherwise empty.</param>
public sealed record CnabFieldInfo(string Name, int Start, int End, CnabFormat Format, string Const)
{
    /// <summary>The number of positions.</summary>
    public int Length => End - Start + 1;
}
