using System.Diagnostics.CodeAnalysis;
using Cnab.Internal;

namespace Cnab;

/// <summary>
/// Attribute-driven codec for fixed-width records such as the FEBRABAN CNAB
/// 240 and CNAB 400 layouts.
/// </summary>
/// <remarks>
/// <para>
/// A record is a class whose properties carry <see cref="CnabFieldAttribute"/>,
/// with positions copied from the bank manual. The first time a type is used
/// its layout is compiled and checked: every position from 1 to the last one
/// must be covered by exactly one field. Gaps, overlaps, out-of-range
/// positions, format/type mismatches and integer properties too narrow for
/// their width are reported together in a <see cref="CnabLayoutException"/>
/// that names the offending properties. The compiled layout, including the
/// property accessors, is cached per type.
/// </para>
/// <para>
/// The layout is resolved from the static type argument, so pass the concrete
/// record type. Values that do not fit their field are errors, never silently
/// truncated.
/// </para>
/// </remarks>
public static class FixedWidth
{
    internal const DynamicallyAccessedMemberTypes RecordMembers =
        DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor;

    /// <summary>Encodes <paramref name="record"/> as one line of exactly <paramref name="lineLength"/> characters, without terminator.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="record">The record to encode.</param>
    /// <param name="lineLength">The record length; the layout must cover positions 1 to it.</param>
    /// <returns>The encoded line.</returns>
    /// <exception cref="CnabLayoutException">The layout is invalid or does not match <paramref name="lineLength"/>.</exception>
    /// <exception cref="CnabFieldException">A value cannot be represented in its field.</exception>
    public static string Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(T record, int lineLength)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(record);
        return Codec.Encode(LayoutCache<T>.Instance, record, lineLength);
    }

    /// <summary>Decodes one line, which must have exactly the layout's length and no terminator.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="line">The line to decode.</param>
    /// <returns>A new record holding the decoded values.</returns>
    /// <exception cref="CnabLayoutException">The layout is invalid.</exception>
    /// <exception cref="CnabFieldException">The line has the wrong length or a field holds an invalid value (<see cref="CnabFieldException.Line"/> is zero).</exception>
    public static T Deserialize<[DynamicallyAccessedMembers(RecordMembers)] T>(string line)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(line);
        var record = new T();
        Codec.Decode(LayoutCache<T>.Instance, line, record);
        return record;
    }

    /// <summary>Decodes one line into an existing record. On error the record may be partially filled.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="line">The line to decode.</param>
    /// <param name="record">The record to fill.</param>
    /// <exception cref="CnabLayoutException">The layout is invalid.</exception>
    /// <exception cref="CnabFieldException">The line has the wrong length or a field holds an invalid value.</exception>
    public static void Populate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string line, T record)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(record);
        Codec.Decode(LayoutCache<T>.Instance, line, record);
    }

    /// <summary>
    /// Returns every problem that prevents <typeparamref name="T"/> from
    /// describing records of <paramref name="lineLength"/> positions; empty
    /// when the layout is valid.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="lineLength">The expected record length.</param>
    /// <returns>The problems, in a stable order.</returns>
    public static IReadOnlyList<string> Validate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(int lineLength)
        where T : class => LayoutCache<T>.Instance.ProblemsFor(lineLength);

    /// <summary>
    /// Verifies that <typeparamref name="T"/> covers exactly positions 1 to
    /// <paramref name="lineLength"/>. Call it from a unit test for each record
    /// type you declare.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="lineLength">The expected record length.</param>
    /// <exception cref="CnabLayoutException">The layout is invalid; every problem is listed.</exception>
    public static void Check<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(int lineLength)
        where T : class => LayoutCache<T>.Instance.CheckWidth(lineLength);

    /// <summary>Returns the record length declared by the layout (its last covered position).</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <returns>The record length.</returns>
    /// <exception cref="CnabLayoutException">The layout is invalid.</exception>
    public static int Width<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>()
        where T : class
    {
        Layout layout = LayoutCache<T>.Instance;
        layout.ThrowIfInvalid();
        return layout.Width;
    }

    /// <summary>Returns the compiled fields ordered by position.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <returns>The fields.</returns>
    /// <exception cref="CnabLayoutException">The layout is invalid.</exception>
    public static IReadOnlyList<CnabFieldInfo> Fields<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>()
        where T : class
    {
        Layout layout = LayoutCache<T>.Instance;
        layout.ThrowIfInvalid();
        return Array.ConvertAll(layout.Fields, f => new CnabFieldInfo(f.Name, f.Start, f.End, f.Format, f.Const));
    }
}
