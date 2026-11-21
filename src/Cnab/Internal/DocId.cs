namespace Cnab.Internal;

/// <summary>Validates Brazilian CPF and CNPJ numbers (digits only).</summary>
internal static class DocId
{
    public const int Invalid = 0;
    public const int Cpf = 1;
    public const int Cnpj = 2;

    /// <summary>
    /// Returns <see cref="Cpf"/> or <see cref="Cnpj"/> (the CNAB "tipo de
    /// inscrição" codes) for a valid 11- or 14-digit number, with check digits
    /// verified and repeated-digit sequences rejected; <see cref="Invalid"/> otherwise.
    /// </summary>
    public static int Classify(string digits)
    {
        if (!Codec.AllDigits(digits))
        {
            return Invalid;
        }

        return digits.Length switch
        {
            11 => !AllSame(digits) && Check(digits, 9, CpfWeight) ? Cpf : Invalid,
            14 => !AllSame(digits) && Check(digits, 12, CnpjWeight) ? Cnpj : Invalid,
            _ => Invalid,
        };
    }

    private static bool AllSame(string s)
    {
        for (int i = 1; i < s.Length; i++)
        {
            if (s[i] != s[0])
            {
                return false;
            }
        }

        return true;
    }

    private static int CpfWeight(int n, int i) => n + 1 - i;

    private static int CnpjWeight(int n, int i)
    {
        // Weights run from n-7 down to 2, then restart at 9.
        int w = n - 7 - i;
        while (w < 2)
        {
            w += 8;
        }

        return w;
    }

    private static bool Check(string d, int first, Func<int, int, int> weight)
    {
        for (int n = first; n <= first + 1; n++)
        {
            int sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += (d[i] - '0') * weight(n, i);
            }

            int r = sum % 11;
            char dv = r < 2 ? '0' : (char)('0' + 11 - r);
            if (dv != d[n])
            {
                return false;
            }
        }

        return true;
    }
}
