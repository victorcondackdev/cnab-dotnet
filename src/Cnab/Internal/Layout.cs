using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace Cnab.Internal;

/// <summary>What a compiled field reads and writes.</summary>
internal enum ValueKind
{
    String,
    Integer,
    Date,
}

/// <summary>One validated field of a record type, with cached accessors.</summary>
internal sealed class CompiledField
{
    public CompiledField(string name, int start, int end, CnabFormat format, string konst, int order)
    {
        Name = name;
        Start = start;
        End = end;
        Format = format;
        Const = konst;
        Order = order;
    }

    public string Name { get; }

    public int Start { get; }

    public int End { get; }

    public int Width => End - Start + 1;

    public CnabFormat Format { get; }

    public string Const { get; }

    /// <summary>Declaration order, used to keep sorting stable and messages deterministic.</summary>
    public int Order { get; }

    public ValueKind Kind { get; set; }

    /// <summary>Largest value the integer property can hold.</summary>
    public long MaxValue { get; set; }

    /// <summary>Display name of the property type, for messages.</summary>
    public string TypeName { get; set; } = string.Empty;

    public Func<object, string>? GetString { get; set; }

    public Action<object, string>? SetString { get; set; }

    public Func<object, long>? GetInteger { get; set; }

    public Action<object, long>? SetInteger { get; set; }

    public Func<object, DateOnly?>? GetDate { get; set; }

    public Action<object, DateOnly?>? SetDate { get; set; }
}

/// <summary>The compiled, validated form of a record type.</summary>
internal sealed class Layout
{
    public Layout(string typeName, CompiledField[] fields, int width, IReadOnlyList<string> problems)
    {
        TypeName = typeName;
        Fields = fields;
        Width = width;
        Problems = problems;
    }

    public string TypeName { get; }

    /// <summary>Fields ordered by start position.</summary>
    public CompiledField[] Fields { get; }

    /// <summary>The last covered position.</summary>
    public int Width { get; }

    /// <summary>Problems found while compiling; empty for a valid layout.</summary>
    public IReadOnlyList<string> Problems { get; }

    public bool IsValid => Problems.Count == 0;

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new CnabLayoutException(TypeName, Problems);
        }
    }

    /// <summary>Problems that prevent the layout from describing records of <paramref name="lineLength"/> positions.</summary>
    public IReadOnlyList<string> ProblemsFor(int lineLength)
    {
        if (!IsValid)
        {
            return Problems;
        }

        if (Width == lineLength)
        {
            return Array.Empty<string>();
        }

        var problems = new List<string>();
        if (Width < lineLength)
        {
            problems.Add(string.Create(GoText.Inv, $"positions {Width + 1}-{lineLength} not covered"));
        }
        else
        {
            foreach (CompiledField f in Fields)
            {
                if (f.End > lineLength)
                {
                    problems.Add(string.Create(GoText.Inv, $"{f.Name} ({f.Start}-{f.End}) exceeds line length {lineLength}"));
                }
            }
        }

        return problems;
    }

    public void CheckWidth(int lineLength)
    {
        IReadOnlyList<string> problems = ProblemsFor(lineLength);
        if (problems.Count > 0)
        {
            throw new CnabLayoutException(TypeName, problems);
        }
    }
}

/// <summary>
/// Compiles each record type once, on first use, and keeps the result for the
/// lifetime of the process. Compilation never throws: problems are stored in
/// the layout and reported every time it is used.
/// </summary>
internal static class LayoutCache<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>
    where T : class
{
    public static readonly Layout Instance = LayoutCompiler.SafeCompile<T>();
}

internal static class LayoutCompiler
{
    /// <summary>Widest num/money field each integer type holds for every digit combination.</summary>
    private static int MaxDigits(Type integerType)
    {
        if (integerType == typeof(byte))
        {
            return 2;
        }

        if (integerType == typeof(short))
        {
            return 4;
        }

        return integerType == typeof(int) ? 9 : 18;
    }

    private static long MaxValueOf(Type integerType)
    {
        if (integerType == typeof(byte))
        {
            return byte.MaxValue;
        }

        if (integerType == typeof(short))
        {
            return short.MaxValue;
        }

        return integerType == typeof(int) ? int.MaxValue : long.MaxValue;
    }

    /// <summary>
    /// The integer type behind <paramref name="type"/> (itself, or an enum's
    /// underlying type) when it is one of the supported ones; otherwise null.
    /// </summary>
    private static Type? IntegerType(Type type)
    {
        Type t = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
        return t == typeof(byte) || t == typeof(short) || t == typeof(int) || t == typeof(long) ? t : null;
    }

    internal static string DisplayName(Type type)
    {
        Type? inner = Nullable.GetUnderlyingType(type);
        if (inner is not null)
        {
            return DisplayName(inner) + "?";
        }

        if (type == typeof(string))
        {
            return "string";
        }

        if (type == typeof(int))
        {
            return "int";
        }

        if (type == typeof(long))
        {
            return "long";
        }

        if (type == typeof(short))
        {
            return "short";
        }

        if (type == typeof(byte))
        {
            return "byte";
        }

        if (type == typeof(bool))
        {
            return "bool";
        }

        return type.Name;
    }

    private static int Depth(Type? type)
    {
        int depth = 0;
        while (type is not null)
        {
            depth++;
            type = type.BaseType;
        }

        return depth;
    }

    /// <summary>Compiles the layout; an unexpected reflection failure becomes a layout problem instead of a type-initialization error.</summary>
    public static Layout SafeCompile<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>()
        where T : class
    {
        try
        {
            return Compile<T>();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return new Layout(typeof(T).Name, Array.Empty<CompiledField>(), 0, new[] { "layout compilation failed: " + e.Message });
        }
    }

    public static Layout Compile<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>()
        where T : class
    {
        Type type = typeof(T);
        var problems = new List<string>();
        var fields = new List<CompiledField>();

        // Base-class properties first, then declaration order, like embedded
        // structs in the Go implementation.
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(p => Depth(p.DeclaringType))
            .ThenBy(p => p.MetadataToken)
            .ToArray();

        foreach (PropertyInfo property in properties)
        {
            CnabFieldAttribute? attribute = property.GetCustomAttribute<CnabFieldAttribute>(inherit: true);
            if (attribute is null)
            {
                continue;
            }

            string? problem = Bind<T>(property, attribute, fields.Count, out CompiledField? field);
            if (problem is not null || field is null)
            {
                problems.Add(property.Name + ": " + (problem ?? "invalid field"));
                continue;
            }

            fields.Add(field);
        }

        if (fields.Count == 0 && problems.Count == 0)
        {
            problems.Add("no CnabField properties");
        }

        CompiledField[] sorted = fields.OrderBy(f => f.Start).ThenBy(f => f.Order).ToArray();
        int next = 1;
        CompiledField? prev = null;
        foreach (CompiledField f in sorted)
        {
            if (f.Start > next && prev is null)
            {
                problems.Add(string.Create(GoText.Inv, $"gap at {next}-{f.Start - 1} before {f.Name}"));
            }
            else if (f.Start > next && prev is not null)
            {
                problems.Add(string.Create(GoText.Inv, $"gap at {next}-{f.Start - 1} between {prev.Name} and {f.Name}"));
            }
            else if (f.Start < next && prev is not null)
            {
                problems.Add(string.Create(GoText.Inv, $"overlap: {prev.Name} ({prev.Start}-{prev.End}) and {f.Name} ({f.Start}-{f.End})"));
            }

            if (prev is null || f.End > prev.End)
            {
                prev = f;
            }

            if (f.End + 1 > next)
            {
                next = f.End + 1;
            }
        }

        return new Layout(type.Name, sorted, next - 1, problems);
    }

    private static string? Bind<T>(PropertyInfo p, CnabFieldAttribute attr, int order, out CompiledField? field)
        where T : class
    {
        field = null;
        if (p.GetIndexParameters().Length != 0 || p.GetMethod is not { IsPublic: true } || p.SetMethod is not { IsPublic: true })
        {
            return "property must have a public getter and setter";
        }

        int start = attr.Start;
        int length = attr.Length;
        if (length < 1)
        {
            return string.Create(GoText.Inv, $"length {length} must be positive");
        }

        if (start < 1)
        {
            return string.Create(GoText.Inv, $"start {start} out of range (positions are 1-based)");
        }

        if ((long)start + length - 1 > int.MaxValue)
        {
            return "field ends beyond the largest position";
        }

        int end = start + length - 1;
        Type type = p.PropertyType;
        string typeName = DisplayName(type);
        Type? integer = IntegerType(type);
        bool isString = type == typeof(string);
        string konst = string.Empty;
        ValueKind kind;

        switch (attr.Format)
        {
            case CnabFormat.Num:
                if (!isString && integer is null)
                {
                    return "num needs a string or integer property, have " + typeName;
                }

                if (integer is not null && length > MaxDigits(integer))
                {
                    return string.Create(GoText.Inv, $"num width {length} too wide for {typeName} (max {MaxDigits(integer)}); use string");
                }

                kind = isString ? ValueKind.String : ValueKind.Integer;
                break;

            case CnabFormat.Money:
                if (integer is null)
                {
                    return "money needs an integer property (cents), have " + typeName;
                }

                if (length > MaxDigits(integer))
                {
                    return string.Create(GoText.Inv, $"money width {length} too wide for {typeName} (max {MaxDigits(integer)})");
                }

                kind = ValueKind.Integer;
                break;

            case CnabFormat.Alfa:
            case CnabFormat.Raw:
                if (!isString)
                {
                    return (attr.Format == CnabFormat.Alfa ? "alfa" : "raw") + " needs a string property, have " + typeName;
                }

                kind = ValueKind.String;
                break;

            case CnabFormat.Date:
                if (type != typeof(DateOnly?))
                {
                    return "date needs a DateOnly? property, have " + typeName;
                }

                if (length != 8 && length != 6)
                {
                    return string.Create(GoText.Inv, $"date width must be 8 (DDMMYYYY) or 6 (DDMMYY), have {length}");
                }

                kind = ValueKind.Date;
                break;

            case CnabFormat.Const:
                if (!isString)
                {
                    return "const needs a string property, have " + typeName;
                }

                konst = attr.Value ?? string.Empty;
                if (konst.Length != length)
                {
                    return string.Create(GoText.Inv, $"const {GoText.Quote(konst)} has length {konst.Length}, field width is {length}");
                }

                kind = ValueKind.String;
                break;

            default:
                return string.Create(GoText.Inv, $"unknown format {(int)attr.Format}");
        }

        var f = new CompiledField(p.Name, start, end, attr.Format, konst, order)
        {
            Kind = kind,
            TypeName = typeName,
        };
        switch (kind)
        {
            case ValueKind.String:
                f.GetString = StringGetter<T>(p);
                f.SetString = StringSetter<T>(p);
                break;
            case ValueKind.Integer:
                f.MaxValue = MaxValueOf(integer!);
                f.GetInteger = IntegerGetter<T>(p);
                f.SetInteger = IntegerSetter<T>(p);
                break;
            default:
                f.GetDate = DateGetter<T>(p);
                f.SetDate = DateSetter<T>(p);
                break;
        }

        field = f;
        return null;
    }

    private static bool IsBindingFailure(Exception e) =>
        e is ArgumentException or NotSupportedException or InvalidOperationException or MemberAccessException;

    // Accessors are compiled to open-instance delegates where the runtime can
    // bind them directly, and fall back to reflection otherwise (enums, init
    // accessors). Either way the property is resolved once per type.

    private static Func<object, string> StringGetter<T>(PropertyInfo p)
        where T : class
    {
        try
        {
            Func<T, string?> d = p.GetMethod!.CreateDelegate<Func<T, string?>>();
            return o => d((T)o) ?? string.Empty;
        }
        catch (Exception e) when (IsBindingFailure(e))
        {
            return o => (string?)p.GetValue(o) ?? string.Empty;
        }
    }

    private static Action<object, string> StringSetter<T>(PropertyInfo p)
        where T : class
    {
        try
        {
            Action<T, string> d = p.SetMethod!.CreateDelegate<Action<T, string>>();
            return (o, v) => d((T)o, v);
        }
        catch (Exception e) when (IsBindingFailure(e))
        {
            return (o, v) => p.SetValue(o, v);
        }
    }

    private static Func<object, DateOnly?> DateGetter<T>(PropertyInfo p)
        where T : class
    {
        try
        {
            Func<T, DateOnly?> d = p.GetMethod!.CreateDelegate<Func<T, DateOnly?>>();
            return o => d((T)o);
        }
        catch (Exception e) when (IsBindingFailure(e))
        {
            return o => (DateOnly?)p.GetValue(o);
        }
    }

    private static Action<object, DateOnly?> DateSetter<T>(PropertyInfo p)
        where T : class
    {
        try
        {
            Action<T, DateOnly?> d = p.SetMethod!.CreateDelegate<Action<T, DateOnly?>>();
            return (o, v) => d((T)o, v);
        }
        catch (Exception e) when (IsBindingFailure(e))
        {
            return (o, v) => p.SetValue(o, v);
        }
    }

    private static Func<object, long> IntegerGetter<T>(PropertyInfo p)
        where T : class
    {
        Type type = p.PropertyType;
        MethodInfo get = p.GetMethod!;
        try
        {
            if (type == typeof(int))
            {
                Func<T, int> d = get.CreateDelegate<Func<T, int>>();
                return o => d((T)o);
            }

            if (type == typeof(long))
            {
                Func<T, long> d = get.CreateDelegate<Func<T, long>>();
                return o => d((T)o);
            }

            if (type == typeof(short))
            {
                Func<T, short> d = get.CreateDelegate<Func<T, short>>();
                return o => d((T)o);
            }

            if (type == typeof(byte))
            {
                Func<T, byte> d = get.CreateDelegate<Func<T, byte>>();
                return o => d((T)o);
            }
        }
        catch (Exception e) when (IsBindingFailure(e))
        {
            // Fall through to reflection.
        }

        return o => Convert.ToInt64(p.GetValue(o), CultureInfo.InvariantCulture);
    }

    private static Action<object, long> IntegerSetter<T>(PropertyInfo p)
        where T : class
    {
        Type type = p.PropertyType;
        MethodInfo set = p.SetMethod!;
        try
        {
            if (type == typeof(int))
            {
                Action<T, int> d = set.CreateDelegate<Action<T, int>>();
                return (o, v) => d((T)o, (int)v);
            }

            if (type == typeof(long))
            {
                Action<T, long> d = set.CreateDelegate<Action<T, long>>();
                return (o, v) => d((T)o, v);
            }

            if (type == typeof(short))
            {
                Action<T, short> d = set.CreateDelegate<Action<T, short>>();
                return (o, v) => d((T)o, (short)v);
            }

            if (type == typeof(byte))
            {
                Action<T, byte> d = set.CreateDelegate<Action<T, byte>>();
                return (o, v) => d((T)o, (byte)v);
            }
        }
        catch (Exception e) when (IsBindingFailure(e))
        {
            // Fall through to reflection.
        }

        if (type.IsEnum)
        {
            return (o, v) => p.SetValue(o, Enum.ToObject(type, v));
        }

        return (o, v) => p.SetValue(o, Convert.ChangeType(v, type, CultureInfo.InvariantCulture));
    }
}
