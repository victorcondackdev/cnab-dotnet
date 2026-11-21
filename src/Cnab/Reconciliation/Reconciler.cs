using System.Text;
using Cnab.Cnab240;
using Cnab.Internal;

namespace Cnab.Reconciliation;

/// <summary>
/// Matches the occurrences of a CNAB 240 retorno against the boletos a
/// company expects to receive, and explains every difference.
/// </summary>
/// <remarks>
/// Each occurrence is classified as paid exactly, paid with an explained
/// adjustment (interest, fine, discount, abatement), paid late, paid with an
/// unexplained divergence, a duplicate settlement, a settlement for an unknown
/// nosso número, a rejection, or a purely informational occurrence. Expected
/// boletos with no settlement are reported as open. Amounts are cents
/// throughout, so totals are exact.
/// </remarks>
public static class Reconciler
{
    /// <summary>Converts a retorno occurrence; its U segment may be missing.</summary>
    /// <param name="occurrence">The occurrence.</param>
    /// <returns>The entry.</returns>
    public static RetornoEntry EntryFrom(Ocorrencia occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        SegmentT t = occurrence.T;
        SegmentU? u = occurrence.U;
        return new RetornoEntry
        {
            NossoNumero = t.NossoNumero,
            NumeroDocumento = t.NumeroDocumento,
            Movimento = t.Movimento,
            Motivos = t.MotivoCodes(),
            Vencimento = t.Vencimento,
            Valor = t.Valor,
            Tarifa = t.Tarifa,
            ValorPago = u?.ValorPago ?? 0,
            ValorLiquido = u?.ValorLiquido ?? 0,
            JurosMulta = u?.JurosMulta ?? 0,
            Desconto = u?.Desconto ?? 0,
            Abatimento = u?.Abatimento ?? 0,
            DataOcorrencia = u?.DataOcorrencia,
            DataCredito = u?.DataCredito,
        };
    }

    /// <summary>Converts every occurrence of a retorno file, in order.</summary>
    /// <param name="file">The retorno.</param>
    /// <returns>The entries.</returns>
    public static IReadOnlyList<RetornoEntry> EntriesFrom(Cnab240File file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.Ocorrencias().Select(EntryFrom).ToList();
    }

    /// <summary>
    /// Normalizes a nosso número for matching: spaces and leading zeros are
    /// removed (<c>00000123 </c> and <c>123</c> match); an all-zero value becomes <c>0</c>.
    /// </summary>
    /// <param name="nossoNumero">The nosso número.</param>
    /// <returns>The matching key.</returns>
    public static string Key(string nossoNumero)
    {
        ArgumentNullException.ThrowIfNull(nossoNumero);
        string k = nossoNumero.Replace(" ", string.Empty, StringComparison.Ordinal).TrimStart('0');
        if (k.Length == 0 && nossoNumero.Length != 0 && nossoNumero.Contains('0', StringComparison.Ordinal))
        {
            return "0";
        }

        return k;
    }

    /// <summary>Matches <paramref name="entries"/> against <paramref name="expected"/>.</summary>
    /// <param name="expected">The boletos the company expects.</param>
    /// <param name="entries">The retorno entries, in file order.</param>
    /// <param name="options">Classification options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The report.</returns>
    /// <exception cref="CnabException">Two expected boletos share a nosso número (<see cref="CnabError.DuplicateExpected"/>).</exception>
    public static ReconcileReport Reconcile(IEnumerable<ExpectedBoleto> expected, IEnumerable<RetornoEntry> entries, ReconcileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(entries);
        options ??= ReconcileOptions.Default;
        List<ExpectedBoleto> expectedList = expected.ToList();
        List<RetornoEntry> entryList = entries.ToList();

        var byKey = new Dictionary<string, ExpectedBoleto>(expectedList.Count, StringComparer.Ordinal);
        long expectedTotal = 0;
        foreach (ExpectedBoleto e in expectedList)
        {
            if (!byKey.TryAdd(Key(e.NossoNumero), e))
            {
                throw new CnabException(
                    CnabError.DuplicateExpected,
                    CnabException.Describe(CnabError.DuplicateExpected) + ": " + GoText.Quote(e.NossoNumero));
            }

            expectedTotal += e.Valor;
        }

        var results = new List<ReconcileResult>(entryList.Count + expectedList.Count);
        var settled = new Dictionary<string, RetornoEntry>(StringComparer.Ordinal);
        long paidTotal = 0;
        long creditedTotal = 0;
        long tariffTotal = 0;
        foreach (RetornoEntry en in entryList)
        {
            string k = Key(en.NossoNumero);
            byKey.TryGetValue(k, out ExpectedBoleto? exp);
            tariffTotal += en.Tarifa;
            string nn = en.NossoNumero.Trim();
            if (en.Movimento.IsLiquidacao())
            {
                creditedTotal += en.ValorLiquido;
                if (settled.TryGetValue(k, out RetornoEntry? prev))
                {
                    results.Add(new ReconcileResult(
                        ReconcileStatus.Duplicate,
                        nn,
                        exp,
                        en,
                        en.ValorPago,
                        "already settled on " + Day(prev.DataOcorrencia) + " for " + Money.FormatBrl(prev.ValorPago)
                            + "; this settlement of " + Money.FormatBrl(en.ValorPago) + " is a duplicate"));
                    continue;
                }

                settled[k] = en;
                if (exp is null)
                {
                    results.Add(new ReconcileResult(
                        ReconcileStatus.Unknown,
                        nn,
                        null,
                        en,
                        en.ValorPago,
                        "settlement of " + Money.FormatBrl(en.ValorPago) + " for a nosso número that is not expected"));
                    continue;
                }

                paidTotal += en.ValorPago;
                results.Add(ClassifyPayment(nn, exp, en, options.GraceDays));
            }
            else if (en.Movimento.IsRejeicao())
            {
                string explanation = en.Movimento.Describe();
                if (en.Motivos.Count > 0)
                {
                    explanation += "; motivos " + string.Join(", ", en.Motivos) + " (see the bank's manual)";
                }

                if (exp is null)
                {
                    explanation += "; nosso número not expected";
                }

                results.Add(new ReconcileResult(ReconcileStatus.Rejected, nn, exp, en, 0, explanation));
            }
            else
            {
                results.Add(new ReconcileResult(ReconcileStatus.Informational, nn, exp, en, 0, en.Movimento.Describe()));
            }
        }

        foreach (ExpectedBoleto e in expectedList)
        {
            if (settled.ContainsKey(Key(e.NossoNumero)))
            {
                continue;
            }

            results.Add(new ReconcileResult(
                ReconcileStatus.Open,
                e.NossoNumero,
                e,
                null,
                0,
                "no settlement in this retorno; due " + Day(e.Vencimento)));
        }

        return new ReconcileReport(results, expectedTotal, paidTotal, creditedTotal, tariffTotal, entryList.Count, expectedList.Count);
    }

    private static string Day(DateOnly? date) => date is DateOnly d ? GoText.Date(d) : "(no date)";

    private static ReconcileResult ClassifyPayment(string nn, ExpectedBoleto exp, RetornoEntry en, int graceDays)
    {
        long difference = en.ValorPago - exp.Valor;
        long explained = en.JurosMulta - en.Desconto - en.Abatimento;
        DateOnly? paidOn = en.DataOcorrencia ?? en.DataCredito;
        bool late = exp.Vencimento is DateOnly dueDate && paidOn is DateOnly paidDate && paidDate.DayNumber > (long)dueDate.DayNumber + graceDays;

        var parts = new List<string>(3);
        if (en.JurosMulta != 0)
        {
            parts.Add("juros/multa " + Money.FormatBrl(en.JurosMulta));
        }

        if (en.Desconto != 0)
        {
            parts.Add("desconto " + Money.FormatBrl(en.Desconto));
        }

        if (en.Abatimento != 0)
        {
            parts.Add("abatimento " + Money.FormatBrl(en.Abatimento));
        }

        string adjustments = string.Join(", ", parts);
        string diff = "paid " + Money.FormatBrl(en.ValorPago) + ", expected " + Money.FormatBrl(exp.Valor)
            + " (difference " + Money.FormatBrl(difference) + ")";

        ReconcileStatus status;
        string explanation;
        if (difference != explained)
        {
            status = ReconcileStatus.Divergent;
            explanation = diff + "; adjustments reported by the bank explain " + Money.FormatBrl(explained);
            if (adjustments.Length != 0)
            {
                explanation += " (" + adjustments + ")";
            }
        }
        else if (late)
        {
            status = ReconcileStatus.PaidLate;
            DateOnly paidDay = paidOn!.Value;
            DateOnly dueDay = exp.Vencimento!.Value;
            int days = paidDay.DayNumber - dueDay.DayNumber;
            explanation = "paid on " + GoText.Date(paidDay) + ", " + GoText.Str(days) + " day(s) after due date " + GoText.Date(dueDay);
            explanation += difference == 0
                ? "; no interest charged"
                : "; " + diff + " explained by " + adjustments;
        }
        else if (difference != 0)
        {
            status = ReconcileStatus.PaidAdjusted;
            explanation = diff + " explained by " + adjustments;
        }
        else
        {
            status = ReconcileStatus.Paid;
            explanation = "paid " + Money.FormatBrl(en.ValorPago) + " on " + Day(paidOn);
        }

        if (en.Valor != 0 && en.Valor != exp.Valor)
        {
            explanation += "; note: bank reports nominal value " + Money.FormatBrl(en.Valor);
        }

        return new ReconcileResult(status, nn, exp, en, difference, explanation);
    }
}

/// <summary>The outcome of a reconciliation.</summary>
public sealed class ReconcileReport
{
    private static readonly (ReconcileStatus Status, string Label)[] SummaryLabels =
    {
        (ReconcileStatus.Paid, "paid exact"),
        (ReconcileStatus.PaidAdjusted, "paid with adjustment"),
        (ReconcileStatus.PaidLate, "paid late"),
        (ReconcileStatus.Divergent, "divergent"),
        (ReconcileStatus.Duplicate, "duplicate"),
        (ReconcileStatus.Unknown, "unknown nosso número"),
        (ReconcileStatus.Rejected, "rejected"),
        (ReconcileStatus.Open, "open"),
        (ReconcileStatus.Informational, "informational"),
    };

    internal ReconcileReport(IReadOnlyList<ReconcileResult> results, long expectedTotal, long paidTotal, long creditedTotal, long tariffTotal, int entryCount, int expectedCount)
    {
        Results = results;
        ExpectedTotal = expectedTotal;
        PaidTotal = paidTotal;
        CreditedTotal = creditedTotal;
        TariffTotal = tariffTotal;
        EntryCount = entryCount;
        ExpectedCount = expectedCount;
    }

    /// <summary>One result per retorno entry, in file order, followed by the open expected boletos.</summary>
    public IReadOnlyList<ReconcileResult> Results { get; }

    /// <summary>Sum of the expected values, in cents.</summary>
    public long ExpectedTotal { get; }

    /// <summary>Sum of the amounts paid over accepted settlements (not duplicates or unknown), in cents.</summary>
    public long PaidTotal { get; }

    /// <summary>Sum of the amounts credited over all settlements, in cents.</summary>
    public long CreditedTotal { get; }

    /// <summary>Sum of the tariffs reported on any entry, in cents.</summary>
    public long TariffTotal { get; }

    /// <summary>The number of retorno entries reconciled.</summary>
    public int EntryCount { get; }

    /// <summary>The number of expected boletos.</summary>
    public int ExpectedCount { get; }

    /// <summary>Reports whether any result is divergent, duplicate, unknown or rejected.</summary>
    public bool NeedsAttention => Results.Any(r => r.Status.NeedsAttention());

    /// <summary>Returns the number of results with <paramref name="status"/>.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The count.</returns>
    public int Count(ReconcileStatus status) => Results.Count(r => r.Status == status);

    /// <summary>Returns the results with <paramref name="status"/>.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The matching results, in report order.</returns>
    public IReadOnlyList<ReconcileResult> Filter(ReconcileStatus status) => Results.Where(r => r.Status == status).ToList();

    /// <summary>
    /// Renders a human-readable overview: counts per status, totals and the
    /// explanation of every result that needs attention or was paid late.
    /// The text is identical to the Go implementation's.
    /// </summary>
    /// <returns>The summary, one item per line, each terminated by <c>\n</c>.</returns>
    public string Summary()
    {
        var b = new StringBuilder();
        b.Append("Reconciliation: ").Append(GoText.Str(EntryCount)).Append(" retorno entries, ")
            .Append(GoText.Str(ExpectedCount)).Append(" expected boletos\n");
        foreach ((ReconcileStatus status, string label) in SummaryLabels)
        {
            b.Append("  ").Append(label.PadRight(22)).Append(' ').Append(GoText.Str(Count(status)).PadLeft(4)).Append('\n');
        }

        b.Append("Expected ").Append(Money.FormatBrl(ExpectedTotal))
            .Append(", paid ").Append(Money.FormatBrl(PaidTotal))
            .Append(", credited ").Append(Money.FormatBrl(CreditedTotal))
            .Append(", tariffs ").Append(Money.FormatBrl(TariffTotal)).Append('\n');
        bool first = true;
        foreach (ReconcileResult r in Results)
        {
            if (!r.Status.NeedsAttention() && r.Status != ReconcileStatus.PaidLate)
            {
                continue;
            }

            if (first)
            {
                b.Append("Attention:\n");
                first = false;
            }

            b.Append("  [").Append(r.Status.ToSnakeCase()).Append("] ").Append(r.NossoNumero).Append(": ").Append(r.Explanation).Append('\n');
        }

        return b.ToString();
    }
}
