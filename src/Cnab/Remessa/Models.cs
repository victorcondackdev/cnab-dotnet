using Cnab.Cnab240;

namespace Cnab.Remessa;

/// <summary>The beneficiary ("beneficiário/cedente") as registered with the bank.</summary>
public sealed record Company
{
    /// <summary>The company name; folded to ASCII and truncated to 30 characters.</summary>
    public string Nome { get; init; } = string.Empty;

    /// <summary>CPF or CNPJ; punctuation (<c>. - /</c> and spaces) is stripped.</summary>
    public string Documento { get; init; } = string.Empty;

    /// <summary>Código do convênio / beneficiário, bank-defined.</summary>
    public string Convenio { get; init; } = string.Empty;

    /// <summary>Agência (digits).</summary>
    public string Agencia { get; init; } = string.Empty;

    /// <summary>Dígito verificador da agência.</summary>
    public string AgenciaDV { get; init; } = string.Empty;

    /// <summary>Conta corrente (digits).</summary>
    public string Conta { get; init; } = string.Empty;

    /// <summary>Dígito verificador da conta.</summary>
    public string ContaDV { get; init; } = string.Empty;

    /// <summary>Dígito verificador da agência/conta.</summary>
    public string AgenciaContaDV { get; init; } = string.Empty;
}

/// <summary>The bank receiving the remessa.</summary>
public sealed record Bank
{
    /// <summary>Compensation code, for example <c>341</c>.</summary>
    public string Codigo { get; init; } = string.Empty;

    /// <summary>The bank name; folded to ASCII and truncated to 30 characters.</summary>
    public string Nome { get; init; } = string.Empty;

    /// <summary>
    /// Layout version written at positions 164-166 of the file header.
    /// Bank-specific; empty means <c>103</c>, a common FEBRABAN value that must
    /// be checked against the bank's manual.
    /// </summary>
    public string VersaoArquivo { get; init; } = string.Empty;

    /// <summary>
    /// Layout version written at positions 14-16 of the batch header.
    /// Bank-specific; empty means <c>060</c>.
    /// </summary>
    public string VersaoLote { get; init; } = string.Empty;
}

/// <summary>The payer ("pagador") of a boleto.</summary>
public sealed record Pagador
{
    /// <summary>Name; required, folded to ASCII and truncated to 40 characters.</summary>
    public string Nome { get; init; } = string.Empty;

    /// <summary>CPF or CNPJ; check digits are verified.</summary>
    public string Documento { get; init; } = string.Empty;

    /// <summary>Address; truncated to 40 characters.</summary>
    public string Endereco { get; init; } = string.Empty;

    /// <summary>Neighbourhood; truncated to 15 characters.</summary>
    public string Bairro { get; init; } = string.Empty;

    /// <summary>CEP: 8 digits, <c>-</c> allowed.</summary>
    public string CEP { get; init; } = string.Empty;

    /// <summary>City; truncated to 15 characters.</summary>
    public string Cidade { get; init; } = string.Empty;

    /// <summary>State, two letters.</summary>
    public string UF { get; init; } = string.Empty;
}

/// <summary>
/// One title to register. Amounts are in cents. Text longer than its CNAB
/// field (names, addresses) is truncated after accent folding; everything else
/// is validated and rejected when invalid.
/// </summary>
public sealed record Boleto
{
    /// <summary>The bank's title identifier, formatted as your bank requires (up to 20 characters).</summary>
    public string NossoNumero { get; init; } = string.Empty;

    /// <summary>"Seu número", 1 to 15 characters.</summary>
    public string NumeroDocumento { get; init; } = string.Empty;

    /// <summary>Carteira; zero means 1 (cobrança simples).</summary>
    public int Carteira { get; init; }

    /// <summary>Espécie; zero means duplicata mercantil.</summary>
    public Especie Especie { get; init; }

    /// <summary>Whether the title is accepted by the payer (aceite A instead of N).</summary>
    public bool Aceite { get; init; }

    /// <summary>Issue date; <see langword="null"/> means the remessa date.</summary>
    public DateOnly? Emissao { get; init; }

    /// <summary>Due date; required.</summary>
    public DateOnly? Vencimento { get; init; }

    /// <summary>Nominal value in cents; must be positive.</summary>
    public long Valor { get; init; }

    /// <summary>Late interest per day in cents; zero means exempt.</summary>
    public long JurosPorDia { get; init; }

    /// <summary>Fixed discount in cents until <see cref="DescontoAte"/>.</summary>
    public long Desconto { get; init; }

    /// <summary>Discount deadline; <see langword="null"/> means <see cref="Vencimento"/> when <see cref="Desconto"/> is positive.</summary>
    public DateOnly? DescontoAte { get; init; }

    /// <summary>Abatimento in cents.</summary>
    public long Abatimento { get; init; }

    /// <summary>Fixed late fine in cents, charged from the day after <see cref="Vencimento"/> (segment R).</summary>
    public long Multa { get; init; }

    /// <summary>Days until protest; zero means "não protestar". At most 99.</summary>
    public int DiasProtesto { get; init; }

    /// <summary>Days until baixa; zero leaves the bank default. At most 999.</summary>
    public int DiasBaixa { get; init; }

    /// <summary>Free identification of the title in the company (up to 25 characters).</summary>
    public string UsoEmpresa { get; init; } = string.Empty;

    /// <summary>Printed message (segment R, mensagem 3; up to 40 characters).</summary>
    public string Mensagem { get; init; } = string.Empty;

    /// <summary>The payer.</summary>
    public Pagador Pagador { get; init; } = new();
}

/// <summary>
/// The remessa builder input was invalid. <see cref="Problems"/> lists every
/// problem found, each prefixed with the boleto's position and nosso número.
/// </summary>
public sealed class RemessaException : CnabException
{
    internal RemessaException(IReadOnlyList<string> problems, Exception? innerException = null)
        : base(CnabError.InvalidRemessa, Describe(CnabError.InvalidRemessa) + ": " + string.Join("\n", problems), innerException)
    {
        Problems = problems;
    }

    /// <summary>Every problem found, in input order.</summary>
    public IReadOnlyList<string> Problems { get; }
}
