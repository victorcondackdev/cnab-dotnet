using System.Diagnostics.CodeAnalysis;
using System.Text;
using Cnab.Internal;

namespace Cnab;

/// <summary>
/// Folds text to the upper-case ASCII accepted by CNAB alfa fields:
/// <c>Avenida São João, nº 10</c> becomes <c>AVENIDA SAO JOAO, NO 10</c>.
/// </summary>
public static class AsciiFolding
{
    private const char NoReplacement = (char)0;

    /// <summary>
    /// Returns <paramref name="text"/> upper-cased with accents folded to ASCII,
    /// exactly as an alfa field is encoded.
    /// </summary>
    /// <param name="text">The text to fold.</param>
    /// <returns>The folded text; never longer than the input.</returns>
    /// <exception cref="CnabException">
    /// The text contains a control character or a character with no ASCII
    /// equivalent (<see cref="CnabError.Charset"/>).
    /// </exception>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string? problem = FoldCore(text, out string folded);
        if (problem is not null)
        {
            throw new CnabException(CnabError.Charset, problem);
        }

        return folded;
    }

    /// <summary>Folds <paramref name="text"/> without throwing.</summary>
    /// <param name="text">The text to fold.</param>
    /// <param name="folded">The folded text, when successful.</param>
    /// <returns><see langword="true"/> when every character could be folded.</returns>
    public static bool TryFold(string text, [NotNullWhen(true)] out string? folded)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (FoldCore(text, out string result) is null)
        {
            folded = result;
            return true;
        }

        folded = null;
        return false;
    }

    /// <summary>
    /// Folds <paramref name="s"/>; returns <see langword="null"/> on success or
    /// the problem, worded as in the Go implementation.
    /// </summary>
    internal static string? FoldCore(string s, out string folded)
    {
        bool ascii = true;
        foreach (char c in s)
        {
            if (c < 0x20 || c > 0x7E || (c >= 'a' && c <= 'z'))
            {
                ascii = false;
                break;
            }
        }

        if (ascii)
        {
            folded = s;
            return null;
        }

        var b = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= 'a' && c <= 'z')
            {
                b.Append((char)(c - 'a' + 'A'));
                continue;
            }

            if (c >= 0x20 && c <= 0x7E)
            {
                b.Append(c);
                continue;
            }

            int cp = c;
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(c, s[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                folded = string.Empty;
                return "invalid UTF-16 (unpaired surrogate)";
            }

            char replacement = Replacement(cp);
            if (replacement == NoReplacement)
            {
                folded = string.Empty;
                return "rune " + GoText.QuoteRune(cp) + " has no ASCII equivalent";
            }

            b.Append(replacement);
        }

        folded = b.ToString();
        return null;
    }

    /// <summary>
    /// Maps Latin-1 letters and a few common symbols to their upper-case ASCII
    /// equivalent; <c>'\0'</c> when there is none. Code points are written as
    /// numbers so the table is independent of the source file encoding.
    /// </summary>
    private static char Replacement(int cp) => cp switch
    {
        0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC4 or 0xC5 => 'A',
        0xE0 or 0xE1 or 0xE2 or 0xE3 or 0xE4 or 0xE5 => 'A',
        0xC8 or 0xC9 or 0xCA or 0xCB => 'E',
        0xE8 or 0xE9 or 0xEA or 0xEB => 'E',
        0xCC or 0xCD or 0xCE or 0xCF => 'I',
        0xEC or 0xED or 0xEE or 0xEF => 'I',
        0xD2 or 0xD3 or 0xD4 or 0xD5 or 0xD6 => 'O',
        0xF2 or 0xF3 or 0xF4 or 0xF5 or 0xF6 => 'O',
        0xD9 or 0xDA or 0xDB or 0xDC => 'U',
        0xF9 or 0xFA or 0xFB or 0xFC => 'U',
        0xC7 or 0xE7 => 'C',
        0xD1 or 0xF1 => 'N',
        0xDD or 0xFD or 0xFF => 'Y',
        0xBA or 0xB0 => 'O', // ordinal masculine, degree sign
        0xAA => 'A', // ordinal feminine
        _ => NoReplacement,
    };
}
