using Alveara.Api.Architecture.Billing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C02: the statement and receipt content without a database. The content depends only on what is at or before the cut-offs (appending to the ledger never changes it), amounts and times are fixed text so
/// the hash is stable, aging counts whole UTC days to the document's own instant and falls into the four buckets at their edges, a receipt shows a payment's applications net of releases, and a CSV cell
/// that a spreadsheet could read as a formula is neutralised.
/// </summary>
public class BillingDocumentBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DocumentParty Ann = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Ann Lee", true);
    private static readonly DocumentParty Bo = new(Guid.Parse("00000000-0000-0000-0000-0000000000b2"), "Bo Lee", false);
    private static readonly DocumentEnvelope Envelope = new(Ann, Bo, "Recorded", "USD");

    private static SnapshotEntry Charge(int n, decimal amount, int daysOld = 0, string code = "D1110") =>
        new(n, BillingKinds.Charge, amount, null, null, null, code, "Cleaning", null, null, T0.AddDays(-daysOld), T0.AddDays(-daysOld));
    private static SnapshotEntry Payment(int n, decimal amount) => new(n, BillingKinds.Payment, -amount, null, null, null, null, null, "Cash", null, T0, T0);
    private static SnapshotEntry Refund(int n, int payment, decimal amount) => new(n, BillingKinds.Refund, amount, null, payment, null, null, null, "Cash", "Patient request", T0, T0);
    private static SnapshotEntry Reversal(int n, SnapshotEntry undone) => new(n, BillingKinds.Reversal, -undone.Amount, undone.Number, null, null, null, null, null, "Error", T0, T0);
    private static FinancialCutOffs Cut(int entries, int allocations = 0, int releases = 0, DateTimeOffset? asOf = null) => new(entries, allocations, releases, 0, asOf ?? T0);

    [Fact]
    public void A_statement_lists_the_ledger_with_running_balances_and_the_totals()
    {
        var snapshot = new LedgerSnapshot([Charge(1, 100m), Charge(2, 50.5m), Payment(3, 60m)], [new(1, 3, 1, 60m)], []);
        var s = FinancialDocumentBuilder.Statement(snapshot, Cut(3, 1), Envelope);
        Assert.Equal(["100.00", "150.50", "90.50"], s.Lines.Select(l => l.BalanceAfter));
        Assert.Equal(("90.50", "90.50", "0.00"), (s.Balance, s.OpenChargeTotal, s.UnappliedTotal));
        Assert.Equal([1, 2], s.OpenCharges.Select(c => c.Number));
        Assert.Equal(("40.00", "60.00"), (s.OpenCharges[0].Open, s.OpenCharges[0].Paid));
        Assert.Equal((Ann, Bo), (s.Patient, s.Addressee));
    }

    [Fact]
    public void Appending_to_the_ledger_never_changes_a_statement_at_earlier_cut_offs()
    {
        var early = new LedgerSnapshot([Charge(1, 100m), Payment(2, 40m)], [new(1, 2, 1, 40m)], []);
        var later = new LedgerSnapshot([Charge(1, 100m), Payment(2, 40m), Refund(3, 2, 10m), Charge(4, 25m), Reversal(5, Payment(2, 40m))], [new(1, 2, 1, 40m), new(2, 2, 1, 1m)], [new(1, 2, 1, 10m, 3)]);
        var a = FinancialDocumentBuilder.HashOf(FinancialDocumentBuilder.Statement(early, Cut(2, 1), Envelope));
        var b = FinancialDocumentBuilder.HashOf(FinancialDocumentBuilder.Statement(later, Cut(2, 1), Envelope));
        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void The_hash_is_stable_whatever_scale_an_amount_carries_and_changes_when_anything_shown_changes()
    {
        var plain = new LedgerSnapshot([Charge(1, 100m)], [], []);
        var scaled = new LedgerSnapshot([Charge(1, 100.00m)], [], []);
        var hash = (LedgerSnapshot s, DocumentEnvelope e) => FinancialDocumentBuilder.HashOf(FinancialDocumentBuilder.Statement(s, Cut(1), e));
        Assert.Equal(hash(plain, Envelope), hash(scaled, Envelope));
        Assert.NotEqual(hash(plain, Envelope), hash(new LedgerSnapshot([Charge(1, 100.01m)], [], []), Envelope));
        Assert.NotEqual(hash(plain, Envelope), hash(plain, Envelope with { Addressee = Ann }));
        Assert.NotEqual(hash(plain, Envelope), hash(plain, Envelope with { Practice = new DocumentPractice("Alder Dental", null, null) }));
        Assert.NotEqual(hash(plain, Envelope with { Practice = new DocumentPractice("Alder Dental", null, null) }), hash(plain, Envelope with { Practice = new DocumentPractice("Alder Dental", "555-0100", null) }));
        Assert.NotEqual(hash(plain, Envelope), hash(plain, Envelope with { Patient = Ann with { Name = "Ann Leigh" } }));
        Assert.NotEqual(FinancialDocumentBuilder.HashOf(FinancialDocumentBuilder.Statement(plain, Cut(1), Envelope)), FinancialDocumentBuilder.HashOf(FinancialDocumentBuilder.Statement(plain, Cut(1, asOf: T0.AddSeconds(1)), Envelope)));
    }

    [Theory]
    [InlineData(0, "Current")]
    [InlineData(30, "Current")]
    [InlineData(31, "31-60")]
    [InlineData(60, "31-60")]
    [InlineData(61, "61-90")]
    [InlineData(90, "61-90")]
    [InlineData(91, "Over 90")]
    public void Aging_falls_into_the_four_buckets_at_their_edges(int days, string bucket)
    {
        var (charges, aging) = FinancialDocumentBuilder.AgingAt(new LedgerSnapshot([Charge(1, 10m, days)], [], []), Cut(1));
        Assert.Equal((days, bucket), (charges[0].AgeDays, charges[0].Bucket));
        var amounts = new[] { aging.Current, aging.Days31To60, aging.Days61To90, aging.Over90 };
        Assert.Equal(1, amounts.Count(a => a == "10.00"));
        Assert.Equal(10m, amounts.Sum(a => decimal.Parse(a, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Aging_is_counted_to_the_documents_own_instant_and_only_open_amounts_age()
    {
        var snapshot = new LedgerSnapshot([Charge(1, 100m, 10), Payment(2, 30m)], [new(1, 2, 1, 30m)], []);
        var now = FinancialDocumentBuilder.AgingAt(snapshot, Cut(2, 1)).Aging;
        var later = FinancialDocumentBuilder.AgingAt(snapshot, Cut(2, 1, asOf: T0.AddDays(25))).Aging;
        Assert.Equal(("70.00", "0.00"), (now.Current, now.Days31To60));                                  // 70 is still open, 10 days old
        Assert.Equal(("0.00", "70.00"), (later.Current, later.Days31To60));                              // the same 70, 35 days old
    }

    [Fact]
    public void Amended_service_dates_age_the_charge_from_the_date_the_snapshot_carries()
    {
        var charge = Charge(1, 40m, 100) with { RecordedAtUtc = T0 };                                     // recorded today, performed 100 days ago
        var aging = FinancialDocumentBuilder.AgingAt(new LedgerSnapshot([charge], [], []), Cut(1)).Aging;
        Assert.Equal("40.00", aging.Over90);
    }

    [Fact]
    public void A_receipt_shows_the_payment_its_applications_net_of_releases_and_what_is_left()
    {
        var snapshot = new LedgerSnapshot([Charge(1, 100m), Charge(2, 100m, code: "D2740"), Payment(3, 150m), Refund(4, 3, 80m)], [new(1, 3, 1, 60m), new(2, 3, 2, 40m)], [new(1, 3, 2, 30m, 4)]);
        var r = FinancialDocumentBuilder.Receipt(snapshot, Cut(4, 2, 1), Envelope, 3);
        Assert.Equal(("150.00", "Cash", "80.00"), (r.Amount, r.Method, r.RefundedToDate));
        Assert.Equal([("1", "60.00"), ("2", "10.00")], r.Applications.Select(a => (a.ChargeNumber.ToString(), a.Amount)));
        Assert.Equal(("0.00", Bo), (r.Unapplied, r.ReceivedFrom));                                        // 150 less 80 refunded (50 unapplied, 30 released) less 70 still applied
    }

    [Fact]
    public void A_receipt_needs_a_payment_that_existed_and_had_not_been_reversed_at_the_cut_offs()
    {
        var payment = Payment(2, 20m);
        var snapshot = new LedgerSnapshot([Charge(1, 100m), payment, Reversal(3, payment)], [], []);
        Assert.Equal("20.00", FinancialDocumentBuilder.Receipt(snapshot, Cut(2), Envelope, 2).Amount);   // before the reversal it is a valid receipt
        Assert.Throws<ArgumentException>(() => FinancialDocumentBuilder.Receipt(snapshot, Cut(3), Envelope, 2));
        Assert.Throws<ArgumentException>(() => FinancialDocumentBuilder.Receipt(snapshot, Cut(1), Envelope, 2));
        Assert.Throws<ArgumentException>(() => FinancialDocumentBuilder.Receipt(snapshot, Cut(3), Envelope, 1));
    }

    [Fact]
    public void A_csv_neutralises_formulas_and_quotes_what_needs_quotes()
    {
        var hostile = new SnapshotEntry(1, BillingKinds.Adjustment, -5m, null, null, null, "=CMD", "Say \"hi\", ok", "+1", "@x\nline", T0, T0);
        var csv = FinancialDocumentBuilder.StatementCsv(FinancialDocumentBuilder.Statement(new LedgerSnapshot([Charge(1, 100m) with { ProcedureCode = "=CMD" }], [], []), Cut(1), Envelope));
        Assert.Contains("'=CMD", csv);
        Assert.DoesNotContain(",=CMD", csv);
        var lines = FinancialDocumentBuilder.StatementCsv(new StatementContent("Statement", 1, "t", "USD", Ann, Bo, [new DocumentLine(1, "Adjustment", "-5.00", "95.00", hostile.ProcedureCode, hostile.ProcedureDescription, hostile.Method, hostile.Reason, "t", null, null, null)], [], [],
            new DocumentAging("0.00", "0.00", "0.00", "0.00"), "95.00", "0.00", "0.00"));
        Assert.Contains("\"Say \"\"hi\"\", ok\"", lines);
        Assert.Contains("'+1", lines);
        Assert.Contains("\"'@x\nline\"", lines);
        Assert.StartsWith("Number,Kind,Amount,BalanceAfter,ProcedureCode,Description,Method,Reason,RecordedAtUtc\r\n", lines);
    }
}
