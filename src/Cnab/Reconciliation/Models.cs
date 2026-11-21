using Cnab.Cnab240;
using Cnab.Internal;

namespace Cnab.Reconciliation;

/// <summary>A boleto the company issued and expects to be paid.</summary>
public sealed record ExpectedBoleto
{
    /// <summary>The nosso número; matched after removing spaces and leading zeros.</summary>
    public string NossoNumero { get; init; } = string.Empty;

    /// <summary>The expected amount in cents.</summary>
    public long Valor { get; init; }

    /// <summary>The due date, when known.</summary>
    public DateOnly? Vencimento { get; init; }

    /// <summary>Free text: invoice, order, customer id.</summary>
    public string Referencia { get; init; } = string.Empty;
}

/// <summary>One retorno occurrence (segment T plus its U), flattened.</summary>
public sealed record RetornoEntry
{
    /// <summary>The nosso número as reported by the bank.</summary>
    public string NossoNumero { get; init; } = string.Empty;

    /// <summary>The número do documento ("seu número").</summary>
    public string NumeroDocumento { get; init; } = string.Empty;

    /// <summary>The occurrence code.</summary>
    public MovimentoRetorno Movimento { get; init; }

    /// <summary>The reason codes (motivos), bank-specific.</summary>
    public IReadOnlyList<string> Motivos { get; init; } = Array.Empty<string>();

    /// <summary>The due date reported by the bank.</summary>
    public DateOnly? Vencimento { get; init; }

    /// <summary>The nominal value reported by the bank, in cents.</summary>
    public long Valor { get; init; }

    /// <summary>The amount paid, in cents.</summary>
    public long ValorPago { get; init; }

    /// <summary>The amount to be credited, in cents.</summary>
    public long ValorLiquido { get; init; }

    /// <summary>Interest, fine and charges, in cents.</summary>
    public long JurosMulta { get; init; }

    /// <summary>Discount granted, in cents.</summary>
    public long Desconto { get; init; }

    /// <summary>Abatimento granted, in cents.</summary>
    public long Abatimento { get; init; }

    /// <summary>Tariff charged, in cents.</summary>
    public long Tarifa { get; init; }

    /// <summary>The occurrence date.</summary>
    public DateOnly? DataOcorrencia { get; init; }

    /// <summary>The credit date.</summary>
    public DateOnly? DataCredito { get; init; }
}

/// <summary>Classifies a reconciliation result. Values are in report order.</summary>
public enum ReconcileStatus
{
    /// <summary>Paid the expected amount on time.</summary>
    Paid = 1,

    /// <summary>Paid on time; the difference is fully explained by juros, desconto or abatimento.</summary>
    PaidAdjusted,

    /// <summary>Paid after the due date (plus grace days).</summary>
    PaidLate,

    /// <summary>Paid, but the difference is not explained by the reported adjustments.</summary>
    Divergent,

    /// <summary>A second settlement of an already settled boleto.</summary>
    Duplicate,

    /// <summary>A settlement for a nosso número not in the expected list.</summary>
    Unknown,

    /// <summary>The bank rejected an entry or instruction.</summary>
    Rejected,

    /// <summary>An expected boleto without settlement in this retorno.</summary>
    Open,

    /// <summary>Other occurrences (entry confirmed, tariffs, baixa, ...).</summary>
    Informational,
}

/// <summary>Helpers for <see cref="ReconcileStatus"/>.</summary>
public static class ReconcileStatusExtensions
{
    /// <summary>
    /// Returns a stable snake_case name, suitable for JSON and CSV
    /// (<c>paid</c>, <c>paid_adjusted</c>, <c>paid_late</c>, ...), identical to the Go implementation.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The name.</returns>
    public static string ToSnakeCase(this ReconcileStatus status) => status switch
    {
        ReconcileStatus.Paid => "paid",
        ReconcileStatus.PaidAdjusted => "paid_adjusted",
        ReconcileStatus.PaidLate => "paid_late",
        ReconcileStatus.Divergent => "divergent",
        ReconcileStatus.Duplicate => "duplicate",
        ReconcileStatus.Unknown => "unknown",
        ReconcileStatus.Rejected => "rejected",
        ReconcileStatus.Open => "open",
        ReconcileStatus.Informational => "informational",
        _ => "status(" + GoText.Str((int)status) + ")",
    };

    /// <summary>Reports whether a result with this status needs human attention (divergent, duplicate, unknown or rejected).</summary>
    /// <param name="status">The status.</param>
    /// <returns><see langword="true"/> when the result needs attention.</returns>
    public static bool NeedsAttention(this ReconcileStatus status) =>
        status is ReconcileStatus.Divergent or ReconcileStatus.Duplicate or ReconcileStatus.Unknown or ReconcileStatus.Rejected;
}

/// <summary>
/// The outcome for one retorno entry or one open expected boleto.
/// </summary>
/// <param name="Status">The classification.</param>
/// <param name="NossoNumero">The nosso número (trimmed for entries).</param>
/// <param name="Expected">The matching expected boleto, or <see langword="null"/>.</param>
/// <param name="Entry">The retorno entry, or <see langword="null"/> for open boletos.</param>
/// <param name="Difference">For settlements, the amount paid minus the amount expected, in cents.</param>
/// <param name="Explanation">A human-readable explanation, identical to the Go implementation.</param>
public sealed record ReconcileResult(
    ReconcileStatus Status,
    string NossoNumero,
    ExpectedBoleto? Expected,
    RetornoEntry? Entry,
    long Difference,
    string Explanation);

/// <summary>Tunes the classification.</summary>
public sealed record ReconcileOptions
{
    /// <summary>The default options.</summary>
    public static ReconcileOptions Default { get; } = new();

    /// <summary>
    /// Tolerates payments up to this many days after the due date before
    /// classifying them as late (for example, due dates falling on weekends or
    /// holidays are payable on the next business day).
    /// </summary>
    public int GraceDays { get; init; }
}
