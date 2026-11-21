using System.Diagnostics.CodeAnalysis;
using System.Text;
using Cnab.Internal;

namespace Cnab;

/// <summary>
/// Formats and parses amounts held as <see cref="long"/> cents, without
/// floating point. Formatting is culture-invariant.
/// </summary>
public static class Money
{
    private static readonly char[] Separators = { '.', ',' };

    /// <summary>Renders cents as <c>1234.56</c> (no thousands separator), with a leading <c>-</c> for negative values.</summary>
    /// <param name="cents">The amount in cents.</param>
    /// <returns>The formatted amount.</returns>
    public static string Format(long cents)
    {
        bool negative = cents < 0;
        ulong u = Magnitude(cents);
        string s = (u / 100).ToString(GoText.Inv) + "." + Two(u % 100);
        return negative ? "-" + s : s;
    }

    /// <summary>Renders cents as <c>R$ 1.234,56</c>, with a leading <c>-</c> for negative values.</summary>
    /// <param name="cents">The amount in cents.</param>
    /// <returns>The formatted amount.</returns>
    public static string FormatBrl(long cents)
    {
        bool negative = cents < 0;
        ulong u = Magnitude(cents);
        string integerPart = (u / 100).ToString(GoText.Inv);
        var b = new StringBuilder(integerPart.Length + 8);
        if (negative)
        {
            b.Append('-');
        }

        b.Append("R$ ");
        for (int i = 0; i < integerPart.Length; i++)
        {
            if (i > 0 && (integerPart.Length - i) % 3 == 0)
            {
                b.Append('.');
            }

            b.Append(integerPart[i]);
        }

        b.Append(',').Append(Two(u % 100));
        return b.ToString();
    }

    /// <summary>
    /// Parses a non-negative decimal amount into cents. Accepts <c>1234</c>,
    /// <c>1234.5</c>, <c>1234.56</c>, <c>1234,56</c>, <c>1.234,56</c>,
    /// <c>1,234.56</c> and an optional <c>R$</c> prefix: the last <c>.</c> or
    /// <c>,</c> followed by one or two digits is the decimal separator and
    /// other separators are grouping.
    /// </summary>
    /// <param name="text">The amount.</param>
    /// <param name="cents">The parsed amount, when successful.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a valid amount.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out long cents)
    {
        cents = 0;
        if (text is null)
        {
            return false;
        }

        string s = text.Trim();
        if (s.StartsWith("R$", StringComparison.Ordinal))
        {
            s = s.Substring(2);
        }

        s = s.Trim();
        if (s.Length == 0)
        {
            return false;
        }

        string integerPart = s;
        string fraction = string.Empty;
        int i = s.LastIndexOfAny(Separators);
        if (i >= 0 && s.Length - i - 1 <= 2)
        {
            integerPart = s.Substring(0, i);
            fraction = s.Substring(i + 1);
            if (fraction.Length == 0)
            {
                return false;
            }
        }

        integerPart = integerPart.Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal);
        if (integerPart.Length == 0)
        {
            integerPart = "0";
        }

        if (!Codec.AllDigits(integerPart) || !Codec.AllDigits(fraction))
        {
            return false;
        }

        fraction = fraction.Length switch
        {
            0 => "00",
            1 => fraction + "0",
            _ => fraction,
        };
        if (integerPart.Length > 16)
        {
            return false;
        }

        // At most 16 + 2 digits: always fits a long.
        long n = 0;
        foreach (char c in integerPart + fraction)
        {
            n = (n * 10) + (c - '0');
        }

        cents = n;
        return true;
    }

    /// <summary>Parses an amount like <see cref="TryParse(string, out long)"/>.</summary>
    /// <param name="text">The amount.</param>
    /// <returns>The amount in cents.</returns>
    /// <exception cref="FormatException">The text is not a valid amount.</exception>
    public static long Parse(string text)
    {
        if (!TryParse(text, out long cents))
        {
            throw new FormatException("money: invalid amount");
        }

        return cents;
    }

    private static ulong Magnitude(long cents) => cents < 0 ? unchecked(0UL - (ulong)cents) : (ulong)cents;

    private static string Two(ulong n) => n < 10 ? "0" + n.ToString(GoText.Inv) : n.ToString(GoText.Inv);
}
