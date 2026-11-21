using System.Globalization;
using System.Text;

namespace Cnab.Internal;

/// <summary>
/// Culture-invariant formatting helpers that reproduce the Go implementation's
/// message formatting (<c>%q</c>, <c>%d</c>, <c>2006-01-02</c>), so error
/// messages and reports are identical in both languages.
/// </summary>
internal static class GoText
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Str(int value) => value.ToString(Inv);

    public static string Str(long value) => value.ToString(Inv);

    /// <summary>Formats a date as <c>yyyy-MM-dd</c>.</summary>
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", Inv);

    /// <summary>
    /// Go <c>%q</c> of a string supplied by the caller (valid Unicode text):
    /// printable characters are kept, others are escaped.
    /// </summary>
    public static string Quote(string s)
    {
        var b = new StringBuilder(s.Length + 2);
        b.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            int cp;
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(c, s[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                cp = 0xFFFD;
            }
            else
            {
                cp = c;
            }

            AppendRune(b, cp, '"');
        }

        b.Append('"');
        return b.ToString();
    }

    /// <summary>
    /// Go <c>%q</c> of text read from a CNAB file. Files are decoded as
    /// Latin-1, one character per byte, so characters 0x80-0xFF stand for
    /// raw bytes and are escaped as <c>\xNN</c>, as Go does for bytes that
    /// are not valid UTF-8.
    /// </summary>
    public static string QuoteBytes(string s)
    {
        var b = new StringBuilder(s.Length + 2);
        b.Append('"');
        foreach (char c in s)
        {
            if (c >= 0x80 && c <= 0xFF)
            {
                b.Append("\\x").Append(((int)c).ToString("x2", Inv));
            }
            else
            {
                AppendRune(b, char.IsSurrogate(c) ? 0xFFFD : c, '"');
            }
        }

        b.Append('"');
        return b.ToString();
    }

    /// <summary>Go <c>%q</c> of a rune (or of a byte value): <c>'x'</c>.</summary>
    public static string QuoteRune(int codePoint)
    {
        var b = new StringBuilder(8);
        b.Append('\'');
        AppendRune(b, codePoint, '\'');
        b.Append('\'');
        return b.ToString();
    }

    private static void AppendRune(StringBuilder b, int cp, char quote)
    {
        if (cp == quote || cp == '\\')
        {
            b.Append('\\').Append((char)cp);
            return;
        }

        if (cp < 0x80)
        {
            if (cp >= 0x20 && cp < 0x7F)
            {
                b.Append((char)cp);
                return;
            }

            string? escape = cp switch
            {
                7 => "\\a",
                8 => "\\b",
                9 => "\\t",
                10 => "\\n",
                11 => "\\v",
                12 => "\\f",
                13 => "\\r",
                _ => null,
            };
            b.Append(escape ?? "\\x" + cp.ToString("x2", Inv));
            return;
        }

        if (IsPrint(cp))
        {
            b.Append(char.ConvertFromUtf32(cp));
            return;
        }

        if (cp < 0x10000)
        {
            b.Append("\\u").Append(cp.ToString("x4", Inv));
        }
        else
        {
            b.Append("\\U").Append(cp.ToString("x8", Inv));
        }
    }

    /// <summary>Approximates Go's <c>unicode.IsPrint</c> for non-ASCII code points.</summary>
    private static bool IsPrint(int cp)
    {
        if (cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
        {
            return false;
        }

        switch (CharUnicodeInfo.GetUnicodeCategory(cp))
        {
            case UnicodeCategory.Control:
            case UnicodeCategory.Format:
            case UnicodeCategory.Surrogate:
            case UnicodeCategory.PrivateUse:
            case UnicodeCategory.OtherNotAssigned:
            case UnicodeCategory.SpaceSeparator:
            case UnicodeCategory.LineSeparator:
            case UnicodeCategory.ParagraphSeparator:
                return false;
            default:
                return true;
        }
    }
}
