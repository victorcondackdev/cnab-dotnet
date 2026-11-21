using Cnab.Cnab240;
using Cnab.Reconciliation;
using static Cnab.Tests.TestData;

namespace Cnab.Tests;

public class ReconcilerTests
{
    private static DateOnly Dec(int day) => D(2026, 12, day);

    private static RetornoEntry Liq(string nn, long pago, DateOnly? on) => new()
    {
        NossoNumero = nn, Movimento = MovimentoRetorno.Liquidacao, ValorPago = pago, ValorLiquido = pago, DataOcorrencia = on,
    };

    private static (List<ExpectedBoleto> Expected, List<RetornoEntry> Entries) Scenario()
    {
        List<ExpectedBoleto> expected = new[] { "0001", "2", "3", "4", "5", "6", "7", "8" }
            .Select(nn => new ExpectedBoleto { NossoNumero = nn, Valor = 1000, Vencimento = Dec(10) })
            .ToList();
        var entries = new List<RetornoEntry>
        {
            new() { NossoNumero = "00000001  ", Movimento = MovimentoRetorno.EntradaConfirmada },
            Liq("1", 1000, Dec(10)),
            Liq("2", 900, Dec(9)) with { Desconto = 100 },
            Liq("3", 1050, Dec(12)) with { JurosMulta = 50 },
            Liq("4", 1200, Dec(10)) with { JurosMulta = 50 },
            Liq("1", 1000, Dec(11)),
            Liq("99", 500, Dec(10)),
            new() { NossoNumero = "5", Movimento = MovimentoRetorno.EntradaRejeitada, Motivos = new[] { "08" } },
            new() { NossoNumero = "77", Movimento = MovimentoRetorno.InstrucaoRejeitada },
            Liq("6", 1000, Dec(11)),
            Liq("7", 1000, Dec(10)) with { Valor = 999 },
            Liq("8", 1000, null) with { DataCredito = Dec(10) },
            new() { NossoNumero = "2", Movimento = MovimentoRetorno.DebitoTarifas, Tarifa = 350 },
        };
        return (expected, entries);
    }

    [Fact]
    public void ClassifiesEveryEntry()
    {
        (List<ExpectedBoleto> expected, List<RetornoEntry> entries) = Scenario();
        ReconcileReport report = Reconciler.Reconcile(expected, entries);
        ReconcileStatus[] want =
        {
            ReconcileStatus.Informational, ReconcileStatus.Paid, ReconcileStatus.PaidAdjusted, ReconcileStatus.PaidLate,
            ReconcileStatus.Divergent, ReconcileStatus.Duplicate, ReconcileStatus.Unknown, ReconcileStatus.Rejected,
            ReconcileStatus.Rejected, ReconcileStatus.PaidLate, ReconcileStatus.Paid, ReconcileStatus.Paid,
            ReconcileStatus.Informational, ReconcileStatus.Open,
        };
        Assert.Equal(want, report.Results.Select(r => r.Status).ToArray());

        var checks = new Dictionary<int, string>
        {
            [2] = "explained by desconto R$ 1,00",
            [3] = "2 day(s) after due date",
            [4] = "explain R$ 0,50 (juros/multa R$ 0,50)",
            [5] = "already settled on 2026-12-10",
            [7] = "motivos 08",
            [8] = "nosso número not expected",
            [9] = "no interest charged",
            [10] = "bank reports nominal value R$ 9,99",
            [13] = "no settlement in this retorno; due 2026-12-10",
        };
        foreach ((int index, string text) in checks)
        {
            Assert.Contains(text, report.Results[index].Explanation, StringComparison.Ordinal);
        }

        Assert.Equal(200, report.Results[4].Difference);
        Assert.Equal(-100, report.Results[2].Difference);
        Assert.Equal(8000, report.ExpectedTotal);
        Assert.Equal(1000 + 900 + 1050 + 1200 + 1000 + 1000 + 1000, report.PaidTotal);
        Assert.Equal(350, report.TariffTotal);
        Assert.True(report.NeedsAttention);
        Assert.Equal(3, report.Count(ReconcileStatus.Paid));
        Assert.Equal(2, report.Filter(ReconcileStatus.Rejected).Count);

        string summary = report.Summary();
        foreach (string s in new[] { "13 retorno entries, 8 expected boletos", "paid exact                3", "[divergent] 4:", "Expected R$ 80,00" })
        {
            Assert.Contains(s, summary, StringComparison.Ordinal);
        }

        // Grace days turn the late payments into on-time ones.
        report = Reconciler.Reconcile(expected, entries, new ReconcileOptions { GraceDays = 2 });
        Assert.Equal(ReconcileStatus.PaidAdjusted, report.Results[3].Status);
        Assert.Equal(ReconcileStatus.Paid, report.Results[9].Status);
    }

    [Fact]
    public void CleanReconciliationNeedsNoAttention()
    {
        ReconcileReport report = Reconciler.Reconcile(
            new[] { new ExpectedBoleto { NossoNumero = "1", Valor = 10 } },
            new[] { Liq("1", 10, null) });
        Assert.False(report.NeedsAttention);
        Assert.DoesNotContain("Attention", report.Summary(), StringComparison.Ordinal);
        Assert.Contains("(no date)", report.Results[0].Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateExpectedIsRejected()
    {
        CnabException e = Assert.Throws<CnabException>(() => Reconciler.Reconcile(
            new[] { new ExpectedBoleto { NossoNumero = "01" }, new ExpectedBoleto { NossoNumero = "1 " } },
            Array.Empty<RetornoEntry>()));
        Assert.Equal(CnabError.DuplicateExpected, e.Error);
        Assert.Equal("reconcile: duplicate expected nosso número: \"1 \"", e.Message);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("0", "0")]
    [InlineData("0000", "0")]
    [InlineData("000123  ", "123")]
    [InlineData("12 34", "1234")]
    [InlineData("A0", "A0")]
    public void Key(string input, string want)
    {
        Assert.Equal(want, Reconciler.Key(input));
    }

    [Fact]
    public void StatusNames()
    {
        Assert.Equal("paid_late", ReconcileStatus.PaidLate.ToSnakeCase());
        Assert.Equal("status(0)", ((ReconcileStatus)0).ToSnakeCase());
        Assert.True(ReconcileStatus.Duplicate.NeedsAttention());
        Assert.False(ReconcileStatus.PaidLate.NeedsAttention());
    }

    [Fact]
    public void EntryFromOccurrence()
    {
        var t = new SegmentT { NossoNumero = "1", Movimento = MovimentoRetorno.Liquidacao, Valor = 10, Tarifa = 2, Motivos = "01" };
        RetornoEntry e = Reconciler.EntryFrom(new Ocorrencia(t, null));
        Assert.Equal("1", e.NossoNumero);
        Assert.Equal(10, e.Valor);
        Assert.Equal(2, e.Tarifa);
        Assert.Equal(0, e.ValorPago);
        Assert.Single(e.Motivos);

        var u = new SegmentU { ValorPago = 12, ValorLiquido = 10, JurosMulta = 2, Desconto = 1, Abatimento = 1, DataOcorrencia = Dec(1), DataCredito = Dec(2) };
        e = Reconciler.EntryFrom(new Ocorrencia(t, u));
        Assert.Equal(12, e.ValorPago);
        Assert.Equal(10, e.ValorLiquido);
        Assert.Equal(2, e.JurosMulta);
        Assert.Equal(Dec(2), e.DataCredito);

        var file = new Cnab240File { Batches = new List<Batch> { new() { Segments = new List<Segment> { t, u } } } };
        RetornoEntry single = Assert.Single(Reconciler.EntriesFrom(file));
        Assert.Equal(12, single.ValorPago);
    }

    [Fact]
    public void SampleRetornoSummaryMatchesTheDocumentation()
    {
        Cnab240File f = Parse("retorno_341.ret");
        var expected = new[]
        {
            new ExpectedBoleto { NossoNumero = "1", Valor = 150000, Vencimento = Dec(10) },
            new ExpectedBoleto { NossoNumero = "2", Valor = 89990, Vencimento = Dec(15) },
            new ExpectedBoleto { NossoNumero = "3", Valor = 25000, Vencimento = Dec(20) },
            new ExpectedBoleto { NossoNumero = "4", Valor = 42050, Vencimento = Dec(20) },
            new ExpectedBoleto { NossoNumero = "5", Valor = 10000, Vencimento = Dec(30) },
        };
        string summary = Reconciler.Reconcile(expected, Reconciler.EntriesFrom(f)).Summary();
        string want = string.Join(
            "\n",
            "Reconciliation: 9 retorno entries, 5 expected boletos",
            "  paid exact                1",
            "  paid with adjustment      1",
            "  paid late                 1",
            "  divergent                 0",
            "  duplicate                 1",
            "  unknown nosso número      1",
            "  rejected                  1",
            "  open                      2",
            "  informational             3",
            "Expected R$ 3.170,40, paid R$ 2.818,80, credited R$ 4.351,30, tariffs R$ 21,00",
            "Attention:",
            "  [rejected] 00000003: Entrada rejeitada; motivos 08, 46 (see the bank's manual)",
            "  [paid_late] 00000002: paid on 2026-12-17, 2 day(s) after due date 2026-12-15; paid R$ 918,80, expected R$ 899,90 (difference R$ 18,90) explained by juros/multa R$ 18,90",
            "  [unknown] 00000099: settlement of R$ 50,00 for a nosso número that is not expected",
            "  [duplicate] 00000001: already settled on 2026-12-10 for R$ 1.500,00; this settlement of R$ 1.500,00 is a duplicate",
            string.Empty);
        Assert.Equal(want, summary);
    }
}
