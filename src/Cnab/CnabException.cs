using Cnab.Internal;

namespace Cnab;

/// <summary>
/// Classifies every problem reported by the library. Each value corresponds
/// to one sentinel error of the Go implementation.
/// </summary>
public enum CnabError
{
    /// <summary>A record type's layout is invalid (gaps, overlaps, bad attributes).</summary>
    Layout = 1,

    /// <summary>A line's length differs from the layout's.</summary>
    LineLength,

    /// <summary>A numeric field holds something other than digits.</summary>
    Digits,

    /// <summary>A value does not fit its field or CLR type.</summary>
    Overflow,

    /// <summary>A negative number; CNAB numeric fields are unsigned.</summary>
    Negative,

    /// <summary>A character that cannot be represented in the field.</summary>
    Charset,

    /// <summary>An invalid or unrepresentable date.</summary>
    Date,

    /// <summary>A constant field that does not hold its literal.</summary>
    Const,

    /// <summary>A raw CNAB 240 segment whose line is not 240 characters.</summary>
    RawLength,

    /// <summary>An input with no records.</summary>
    Empty,

    /// <summary>A line length other than 240 or 400, or a layout that does not match the call.</summary>
    Format,

    /// <summary>Records out of order: missing header or trailer, segment outside a batch, unexpected record type.</summary>
    Structure,

    /// <summary>A trailer count or total that does not match the records.</summary>
    Count,

    /// <summary>A batch or record sequence number out of order.</summary>
    Sequence,

    /// <summary>A record whose bank code differs from the file header's.</summary>
    Bank,

    /// <summary>Remessa builder input that cannot produce a valid file.</summary>
    InvalidRemessa,

    /// <summary>Two expected boletos with the same nosso número.</summary>
    DuplicateExpected,
}

/// <summary>
/// Base class of every exception the library throws for invalid input or
/// invalid layouts. <see cref="Error"/> tells what went wrong; the message is
/// identical to the one produced by the Go implementation.
/// </summary>
public class CnabException : Exception
{
    /// <summary>Initializes the exception with the default message for <paramref name="error"/>.</summary>
    /// <param name="error">The error classification.</param>
    public CnabException(CnabError error)
        : this(error, Describe(error), null)
    {
    }

    /// <summary>Initializes the exception with a message.</summary>
    /// <param name="error">The error classification.</param>
    /// <param name="message">The error message.</param>
    public CnabException(CnabError error, string message)
        : this(error, message, null)
    {
    }

    /// <summary>Initializes the exception with a message and an inner exception.</summary>
    /// <param name="error">The error classification.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public CnabException(CnabError error, string message, Exception? innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    /// <summary>The error classification.</summary>
    public CnabError Error { get; }

    /// <summary>
    /// Returns the generic message of an error classification, for example
    /// <c>cnab: trailer count mismatch</c>.
    /// </summary>
    /// <param name="error">The error classification.</param>
    /// <returns>The message.</returns>
    public static string Describe(CnabError error) => error switch
    {
        CnabError.Layout => "fixedwidth: invalid layout",
        CnabError.LineLength => "fixedwidth: wrong line length",
        CnabError.Digits => "fixedwidth: non-digit in numeric field",
        CnabError.Overflow => "fixedwidth: value does not fit",
        CnabError.Negative => "fixedwidth: negative value",
        CnabError.Charset => "fixedwidth: unsupported character",
        CnabError.Date => "fixedwidth: invalid date",
        CnabError.Const => "fixedwidth: constant mismatch",
        CnabError.RawLength => "cnab240: raw segment must be 240 bytes",
        CnabError.Empty => "cnab: empty file",
        CnabError.Format => "cnab: unsupported or mixed record length",
        CnabError.Structure => "cnab: invalid file structure",
        CnabError.Count => "cnab: trailer count mismatch",
        CnabError.Sequence => "cnab: sequence number mismatch",
        CnabError.Bank => "cnab: bank code mismatch",
        CnabError.InvalidRemessa => "cnab: invalid remessa data",
        CnabError.DuplicateExpected => "reconcile: duplicate expected nosso número",
        _ => "cnab: error",
    };
}

/// <summary>
/// A value that could not be encoded or decoded, located by field name and
/// 1-based inclusive positions, as printed in bank manuals.
/// </summary>
/// <remarks>
/// The message reads like <c>cnab: line 3: field Valor (86-100) value "00000000000A000": must contain only digits</c>.
/// <see cref="Line"/> is zero when the error comes straight from
/// <see cref="FixedWidth"/> and is filled in by the file parsers.
/// <see cref="Context"/> names the record when the error comes from a writer
/// (for example <c>batch 1 header</c>).
/// </remarks>
public sealed class CnabFieldException : CnabException
{
    private readonly bool _valueFromFile;

    internal CnabFieldException(CnabError error, string field, int start, int end, string value, string reason, bool valueFromFile)
        : this(error, field, start, end, value, reason, valueFromFile, 0, null)
    {
    }

    private CnabFieldException(CnabError error, string field, int start, int end, string value, string reason, bool valueFromFile, int line, string? context)
        : base(error, BuildMessage(field, start, end, value, reason, valueFromFile, line, context))
    {
        Field = field;
        Start = start;
        End = end;
        Value = value;
        Reason = reason;
        Line = line;
        Context = context;
        _valueFromFile = valueFromFile;
    }

    /// <summary>The 1-based line number in a file, or zero when unknown.</summary>
    public int Line { get; }

    /// <summary>The property name of the field (or the record type for line-length errors).</summary>
    public string Field { get; }

    /// <summary>The first position of the field, 1-based.</summary>
    public int Start { get; }

    /// <summary>The last position of the field, 1-based and inclusive.</summary>
    public int End { get; }

    /// <summary>The offending value; empty when there is none.</summary>
    public string Value { get; }

    /// <summary>Why the value was rejected, for example <c>must contain only digits</c>.</summary>
    public string Reason { get; }

    /// <summary>The record being written when the error occurred, or <see langword="null"/>.</summary>
    public string? Context { get; }

    internal CnabFieldException WithLine(int line) =>
        new(Error, Field, Start, End, Value, Reason, _valueFromFile, line, Context);

    internal CnabFieldException WithContext(string context) =>
        new(Error, Field, Start, End, Value, Reason, _valueFromFile, Line, Context is null ? context : context + ": " + Context);

    private static string BuildMessage(string field, int start, int end, string value, string reason, bool valueFromFile, int line, string? context)
    {
        string message = "cnab: ";
        if (line > 0)
        {
            message += "line " + GoText.Str(line) + ": ";
        }

        message += "field " + field + " (" + GoText.Str(start) + "-" + GoText.Str(end) + ")";
        if (value.Length != 0)
        {
            message += " value " + (valueFromFile ? GoText.QuoteBytes(value) : GoText.Quote(value));
        }

        message += ": " + reason;
        return context is null ? message : context + ": " + message;
    }
}

/// <summary>
/// A structural problem located at a line of a file: counts, sequences,
/// record order, bank codes or line lengths.
/// </summary>
/// <remarks>
/// The message reads like <c>cnab: line 14: cnab: trailer count mismatch: batch 1 trailer reports 99 records, counted 13</c>.
/// </remarks>
public sealed class CnabLineException : CnabException
{
    internal CnabLineException(int line, CnabError error, string detail)
        : base(error, "cnab: line " + GoText.Str(line) + ": " + Describe(error) + ": " + detail)
    {
        Line = line;
        Detail = detail;
    }

    internal CnabLineException(int line, CnabException inner)
        : base(inner.Error, "cnab: line " + GoText.Str(line) + ": " + inner.Message, inner)
    {
        Line = line;
        Detail = inner.Message;
    }

    /// <summary>The 1-based line number.</summary>
    public int Line { get; }

    /// <summary>The specific problem, without the line prefix.</summary>
    public string Detail { get; }
}

/// <summary>
/// An invalid record layout: every problem found while compiling the type is
/// listed in <see cref="Problems"/>.
/// </summary>
/// <remarks>
/// The message reads like <c>fixedwidth: invalid layout Typo: overlap: Banco (1-3) and Lote (3-6); gap at 7-7 between Lote and Tipo</c>.
/// </remarks>
public sealed class CnabLayoutException : CnabException
{
    internal CnabLayoutException(string typeName, IReadOnlyList<string> problems)
        : base(CnabError.Layout, "fixedwidth: invalid layout " + typeName + ": " + string.Join("; ", problems))
    {
        TypeName = typeName;
        Problems = problems;
    }

    /// <summary>The name of the record type.</summary>
    public string TypeName { get; }

    /// <summary>Every problem found, in a stable order.</summary>
    public IReadOnlyList<string> Problems { get; }
}
