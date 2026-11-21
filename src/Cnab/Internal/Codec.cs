namespace Cnab.Internal;

/// <summary>Encodes and decodes records with a compiled <see cref="Layout"/>.</summary>
internal static class Codec
{
    public static string Encode(Layout layout, object record, int lineLength)
    {
        layout.CheckWidth(lineLength);
        char[] buffer = new char[lineLength];
        foreach (CompiledField f in layout.Fields)
        {
            EncodeField(f, record, buffer.AsSpan(f.Start - 1, f.Width));
        }

        return new string(buffer);
    }

    public static void Decode(Layout layout, string line, object target)
    {
        layout.ThrowIfInvalid();
        if (line.Length != layout.Width)
        {
            throw new CnabFieldException(
                CnabError.LineLength,
                layout.TypeName,
                1,
                layout.Width,
                string.Empty,
                string.Create(GoText.Inv, $"line has {line.Length} bytes, want {layout.Width}"),
                valueFromFile: true);
        }

        foreach (CompiledField f in layout.Fields)
        {
            DecodeField(f, line.AsSpan(f.Start - 1, f.Width), target);
        }
    }

    private static CnabFieldException Error(CompiledField f, string value, CnabError error, string reason, bool fromFile) =>
        new(error, f.Name, f.Start, f.End, value, reason, fromFile);

    private static void EncodeField(CompiledField f, object record, Span<char> dst)
    {
        int w = dst.Length;
        switch (f.Format)
        {
            case CnabFormat.Const:
            {
                string s = f.GetString!(record);
                if (s.Length != 0 && !string.Equals(s, f.Const, StringComparison.Ordinal))
                {
                    throw Error(f, s, CnabError.Const, "must be empty or " + GoText.Quote(f.Const), false);
                }

                f.Const.AsSpan().CopyTo(dst);
                return;
            }

            case CnabFormat.Alfa:
            {
                string s = f.GetString!(record);
                string? problem = AsciiFolding.FoldCore(s, out string folded);
                if (problem is not null)
                {
                    throw Error(f, s, CnabError.Charset, problem, false);
                }

                if (folded.Length > w)
                {
                    throw Error(f, folded, CnabError.Overflow, "longer than " + GoText.Str(w) + " bytes", false);
                }

                folded.AsSpan().CopyTo(dst);
                dst.Slice(folded.Length).Fill(' ');
                return;
            }

            case CnabFormat.Raw:
            {
                string s = f.GetString!(record);
                foreach (char c in s)
                {
                    if (c == '\r' || c == '\n')
                    {
                        throw Error(f, s, CnabError.Charset, "line terminator inside field", false);
                    }

                    if (c > (char)0xFF)
                    {
                        throw Error(f, s, CnabError.Charset, "rune " + GoText.QuoteRune(c) + " is outside Latin-1", false);
                    }
                }

                if (s.Length > w)
                {
                    throw Error(f, s, CnabError.Overflow, "longer than " + GoText.Str(w) + " bytes", false);
                }

                s.AsSpan().CopyTo(dst);
                dst.Slice(s.Length).Fill(' ');
                return;
            }

            case CnabFormat.Num:
            case CnabFormat.Money:
            {
                string digits;
                if (f.Kind == ValueKind.String)
                {
                    digits = f.GetString!(record);
                    foreach (char c in digits)
                    {
                        if (c < '0' || c > '9')
                        {
                            throw Error(f, digits, CnabError.Digits, "must contain only digits", false);
                        }
                    }
                }
                else
                {
                    long n = f.GetInteger!(record);
                    if (n < 0)
                    {
                        throw Error(f, GoText.Str(n), CnabError.Negative, "negative values are not representable", false);
                    }

                    digits = GoText.Str(n);
                }

                if (digits.Length > w)
                {
                    throw Error(f, digits, CnabError.Overflow, "more than " + GoText.Str(w) + " digits", false);
                }

                dst.Slice(0, w - digits.Length).Fill('0');
                digits.AsSpan().CopyTo(dst.Slice(w - digits.Length));
                return;
            }

            default:
            {
                DateOnly? value = f.GetDate!(record);
                if (value is not DateOnly d)
                {
                    dst.Fill('0');
                    return;
                }

                if (w == 6 && (d.Year < 1969 || d.Year > 2068))
                {
                    throw Error(f, GoText.Date(d), CnabError.Date, "year not representable as DDMMYY (1969-2068)", false);
                }

                PutDigits(dst.Slice(0, 2), d.Day);
                PutDigits(dst.Slice(2, 2), d.Month);
                PutDigits(dst.Slice(4), w == 8 ? d.Year : d.Year % 100);
                return;
            }
        }
    }

    private static void DecodeField(CompiledField f, ReadOnlySpan<char> s, object target)
    {
        switch (f.Format)
        {
            case CnabFormat.Const:
                if (!s.SequenceEqual(f.Const.AsSpan()))
                {
                    throw Error(f, s.ToString(), CnabError.Const, "expected constant " + GoText.Quote(f.Const), true);
                }

                f.SetString!(target, f.Const);
                return;

            case CnabFormat.Alfa:
                f.SetString!(target, s.TrimEnd(' ').ToString());
                return;

            case CnabFormat.Raw:
                f.SetString!(target, s.ToString());
                return;

            case CnabFormat.Num:
            case CnabFormat.Money:
            {
                if (All(s, ' '))
                {
                    if (f.Kind == ValueKind.String)
                    {
                        f.SetString!(target, string.Empty);
                    }
                    else
                    {
                        f.SetInteger!(target, 0);
                    }

                    return;
                }

                if (!AllDigits(s))
                {
                    throw Error(f, s.ToString(), CnabError.Digits, "must contain only digits", true);
                }

                if (f.Kind == ValueKind.String)
                {
                    f.SetString!(target, s.ToString());
                    return;
                }

                // Widths are capped at 18 digits by the layout compiler, so the
                // accumulation cannot overflow a long.
                long n = 0;
                foreach (char c in s)
                {
                    n = (n * 10) + (c - '0');
                }

                if (n > f.MaxValue)
                {
                    throw Error(f, s.ToString(), CnabError.Overflow, "does not fit " + f.TypeName, true);
                }

                f.SetInteger!(target, n);
                return;
            }

            default:
            {
                if (All(s, '0') || All(s, ' '))
                {
                    f.SetDate!(target, null);
                    return;
                }

                if (!AllDigits(s))
                {
                    throw Error(f, s.ToString(), CnabError.Date, "must contain only digits", true);
                }

                if (!TryParseDate(s, out DateOnly date))
                {
                    throw Error(f, s.ToString(), CnabError.Date, "not a valid date", true);
                }

                f.SetDate!(target, date);
                return;
            }
        }
    }

    /// <summary>Parses <c>DDMMYYYY</c> or <c>DDMMYY</c> (69-99 is 19xx, 00-68 is 20xx).</summary>
    internal static bool TryParseDate(ReadOnlySpan<char> s, out DateOnly date)
    {
        date = default;
        int day = Number(s.Slice(0, 2));
        int month = Number(s.Slice(2, 2));
        int year;
        if (s.Length == 8)
        {
            year = Number(s.Slice(4, 4));
        }
        else
        {
            int yy = Number(s.Slice(4, 2));
            year = yy >= 69 ? 1900 + yy : 2000 + yy;
        }

        if (year < 1 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    private static int Number(ReadOnlySpan<char> s)
    {
        int n = 0;
        foreach (char c in s)
        {
            n = (n * 10) + (c - '0');
        }

        return n;
    }

    internal static bool All(ReadOnlySpan<char> s, char c)
    {
        foreach (char x in s)
        {
            if (x != c)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool AllDigits(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Writes <paramref name="n"/> zero-padded into <paramref name="dst"/> (it must fit).</summary>
    internal static void PutDigits(Span<char> dst, int n)
    {
        for (int i = dst.Length - 1; i >= 0; i--)
        {
            dst[i] = (char)('0' + (n % 10));
            n /= 10;
        }
    }
}
