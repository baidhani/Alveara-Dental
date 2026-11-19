using Alveara.Api.Architecture.Billing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A1 (A1-DECISION-01), without a database: version-1 statements and receipts rebuild byte for byte as the original builder made them (against bytes captured BEFORE version 2 existed);
/// version 2 ages in practice-local calendar days from a frozen basis (evening services, every bucket edge, both DST changes, a year end, zones behind and ahead of UTC) with every amount unchanged; the
/// basis is checked against the document; and an unknown template version is refused. Negative control: a boundary-affected version-1 document built by the version-2 rule no longer matches.
/// </summary>
public class FinancialDocumentTemplateVersionTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
    private static readonly DocumentParty Ann = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Ann Lee", true);
    private static readonly DocumentEnvelope Plain = new(Ann, Ann, "Self", "USD");

    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);
    private static SnapshotEntry Charge(int n, decimal amount, DateTimeOffset service) => new(n, BillingKinds.Charge, amount, null, null, null, "D1110", "Cleaning", null, null, service, service);
    private static FinancialCutOffs At(DateTimeOffset asOf, int entries = 1) => new(entries, 0, 0, 0, asOf);

    private static object BuildV1(FinancialDocumentV1Cases.Case c) => c.Type == FinancialDocumentTypes.Statement
        ? FinancialDocumentBuilder.Statement(c.Snapshot, c.CutOffs, c.Envelope, 1)
        : FinancialDocumentBuilder.Receipt(c.Snapshot, c.CutOffs, c.Envelope, c.PaymentNumber!.Value, 1);

    private static StatementContent V2(LedgerSnapshot s, FinancialCutOffs cut, TimeZoneInfo zone, DocumentEnvelope? envelope = null) =>
        FinancialDocumentBuilder.Statement(s, cut, (envelope ?? Plain) with { AgingBasis = FinancialDocumentBuilder.PracticeBasis(s, cut, zone) }, 2);

    // ---------- version 1 is frozen ----------

    [Fact]
    public void Every_captured_version_1_document_rebuilds_with_identical_canonical_bytes_and_hash()
    {
        var captured = FinancialDocumentV1BaselineFile.Load().ToDictionary(d => d.Name);
        Assert.Equal(FinancialDocumentV1Cases.All.Count, captured.Count);
        foreach (var c in FinancialDocumentV1Cases.All)
        {
            var json = FinancialDocumentBuilder.CanonicalJson(BuildV1(c));
            Assert.True(captured[c.Name].CanonicalJson == json, $"{c.Name}: version-1 bytes changed");
            Assert.Equal(captured[c.Name].Sha256, FinancialDocumentBuilder.Hash(json));
        }
    }

    [Fact]
    public void The_verbatim_original_builder_copy_reproduces_the_captured_bytes()
    {
        var captured = FinancialDocumentV1BaselineFile.Load().ToDictionary(d => d.Name);
        foreach (var c in FinancialDocumentV1Cases.All)
        {
            object content = c.Type == FinancialDocumentTypes.Statement
                ? OriginalFinancialDocumentBuilderV1.Statement(c.Snapshot, c.CutOffs, c.Envelope)
                : OriginalFinancialDocumentBuilderV1.Receipt(c.Snapshot, c.CutOffs, c.Envelope, c.PaymentNumber!.Value);
            Assert.Equal(captured[c.Name].CanonicalJson, OriginalFinancialDocumentBuilderV1.CanonicalJson(content));
        }
    }

    [Fact]
    public void A_version_1_statement_ignores_an_aging_basis_even_if_one_is_present()
    {
        var c = FinancialDocumentV1Cases.All.First(x => x.Name == "statement-evening-utc-age-30");
        var withBasis = c.Envelope with { AgingBasis = FinancialDocumentBuilder.PracticeBasis(c.Snapshot, c.CutOffs, Chicago) };
        Assert.Equal(FinancialDocumentBuilder.CanonicalJson(BuildV1(c)), FinancialDocumentBuilder.CanonicalJson(FinancialDocumentBuilder.Statement(c.Snapshot, c.CutOffs, withBasis, 1)));
    }

    [Fact]
    public void Negative_control_a_boundary_affected_version_1_statement_built_by_the_version_2_rule_no_longer_matches()
    {
        var captured = FinancialDocumentV1BaselineFile.Load().ToDictionary(d => d.Name);
        foreach (var name in new[] { "statement-evening-utc-age-30", "statement-evening-utc-age-60", "statement-evening-utc-age-90" })
        {
            var c = FinancialDocumentV1Cases.All.First(x => x.Name == name);
            var asV2 = V2(c.Snapshot, c.CutOffs, Chicago, c.Envelope);
            Assert.NotEqual(captured[name].Sha256, FinancialDocumentBuilder.HashOf(asV2));
            Assert.NotEqual(FinancialDocumentBuilder.Bucket(int.Parse(name[^2..])), asV2.OpenCharges[0].Bucket);   // the practice-day age crosses the edge
        }
    }

    // ---------- version 2: practice-local calendar days ----------

    [Fact]
    public void An_evening_service_ages_one_day_more_by_practice_days_than_by_UTC_days()
    {
        var s = new LedgerSnapshot([Charge(1, 10m, FinancialDocumentV1Cases.EveningService)], [], []);   // 1 Jan 22:42 Chicago = 2 Jan 04:42 UTC
        var cut = At(Utc(2030, 1, 12, 15, 0));                                                            // 12 Jan 09:00 Chicago
        Assert.Equal(10, FinancialDocumentBuilder.Statement(s, cut, Plain, 1).OpenCharges[0].AgeDays);
        Assert.Equal(11, V2(s, cut, Chicago).OpenCharges[0].AgeDays);
    }

    [Theory]
    [InlineData(30, "Current")]
    [InlineData(31, "31-60")]
    [InlineData(60, "31-60")]
    [InlineData(61, "61-90")]
    [InlineData(90, "61-90")]
    [InlineData(91, "Over 90")]
    public void Version_2_buckets_turn_on_practice_day_edges(int practiceDays, string bucket)
    {
        var service = FinancialDocumentV1Cases.EveningService;                                            // practice date 2030-01-01
        var asOf = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.FromHours(-6)).AddDays(practiceDays);   // 09:00 CST + N days (after 10 Mar that instant is 10:00 CDT: still the same practice date)
        var s = new LedgerSnapshot([Charge(1, 10m, service)], [], []);
        var statement = V2(s, At(asOf), Chicago);
        Assert.Equal((practiceDays, bucket), (statement.OpenCharges[0].AgeDays, statement.OpenCharges[0].Bucket));
        Assert.Equal("10.00", new[] { statement.Aging.Current, statement.Aging.Days31To60, statement.Aging.Days61To90, statement.Aging.Over90 }.Single(a => a != "0.00"));
    }

    [Fact]
    public void Each_instant_is_converted_with_its_own_offset_across_the_spring_change()
    {
        // service 9 Mar 2030 21:30 CST (-6) = 10 Mar 03:30 UTC; as of 10 Apr 05:30 UTC = 10 Apr 00:30 CDT (-5). One fixed -6 offset would put the as-of on 9 Apr.
        var s = new LedgerSnapshot([Charge(1, 10m, Utc(2030, 3, 10, 3, 30))], [], []);
        var basis = FinancialDocumentBuilder.PracticeBasis(s, At(Utc(2030, 4, 10, 5, 30)), Chicago);
        Assert.Equal(("2030-03-09", "2030-04-10"), (basis.ServiceDays[0].ServiceLocalDate, basis.AsOfLocalDate));
        Assert.Equal(32, V2(s, At(Utc(2030, 4, 10, 5, 30)), Chicago).OpenCharges[0].AgeDays);
    }

    [Fact]
    public void Each_instant_is_converted_with_its_own_offset_across_the_autumn_change()
    {
        // service 3 Nov 2030 05:30 UTC = 00:30 CDT (-5) on 3 Nov (one fixed -6 offset would say 2 Nov); as of 3 Dec 18:00 UTC = 12:00 CST on 3 Dec
        var s = new LedgerSnapshot([Charge(1, 10m, Utc(2030, 11, 3, 5, 30))], [], []);
        var basis = FinancialDocumentBuilder.PracticeBasis(s, At(Utc(2030, 12, 3, 18, 0)), Chicago);
        Assert.Equal(("2030-11-03", "2030-12-03"), (basis.ServiceDays[0].ServiceLocalDate, basis.AsOfLocalDate));
        Assert.Equal(30, V2(s, At(Utc(2030, 12, 3, 18, 0)), Chicago).OpenCharges[0].AgeDays);
    }

    [Fact]
    public void A_year_end_service_counts_from_the_practice_day_it_happened()
    {
        var s = new LedgerSnapshot([Charge(1, 10m, Utc(2031, 1, 1, 2, 0))], [], []);                      // 31 Dec 2030 20:00 Chicago
        var cut = At(Utc(2031, 1, 31, 15, 0));                                                            // 31 Jan 2031 09:00 Chicago
        Assert.Equal(("2030-12-31", 31), (FinancialDocumentBuilder.PracticeBasis(s, cut, Chicago).ServiceDays[0].ServiceLocalDate, V2(s, cut, Chicago).OpenCharges[0].AgeDays));
        Assert.Equal(30, FinancialDocumentBuilder.Statement(s, cut, Plain, 1).OpenCharges[0].AgeDays);
    }

    [Fact]
    public void A_zone_ahead_of_UTC_moves_a_morning_service_to_the_next_practice_day()
    {
        var s = new LedgerSnapshot([Charge(1, 10m, Utc(2030, 5, 31, 23, 0))], [], []);                   // 1 Jun 08:00 Tokyo
        var cut = At(Utc(2030, 7, 1, 3, 0));                                                              // 1 Jul 12:00 Tokyo
        Assert.Equal(30, V2(s, cut, Tokyo).OpenCharges[0].AgeDays);                                       // Tokyo: 1 Jun -> 1 Jul
        Assert.Equal(31, FinancialDocumentBuilder.Statement(s, cut, Plain, 1).OpenCharges[0].AgeDays);   // UTC: 31 May -> 1 Jul
    }

    [Fact]
    public void Version_2_changes_only_ages_buckets_version_and_basis_never_an_amount()
    {
        var c = FinancialDocumentV1Cases.All.First(x => x.Name == "statement-rich");
        var v1 = FinancialDocumentBuilder.Statement(c.Snapshot, c.CutOffs, c.Envelope, 1);
        var v2 = V2(c.Snapshot, c.CutOffs, Chicago, c.Envelope);
        Assert.Equal((v1.Balance, v1.OpenChargeTotal, v1.UnappliedTotal), (v2.Balance, v2.OpenChargeTotal, v2.UnappliedTotal));
        Assert.Equal(v1.Lines, v2.Lines);
        Assert.Equal(v1.Unapplied, v2.Unapplied);
        Assert.Equal(v1.OpenCharges.Select(o => (o.Number, o.Amount, o.Paid, o.Open, o.ServiceDateUtc)), v2.OpenCharges.Select(o => (o.Number, o.Amount, o.Paid, o.Open, o.ServiceDateUtc)));
        var total = (DocumentAging a) => new[] { a.Current, a.Days31To60, a.Days61To90, a.Over90 }.Sum(x => decimal.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(total(v1.Aging), total(v2.Aging));
        Assert.Equal((2, "America/Chicago"), (v2.TemplateVersion, v2.AgingBasis!.TimeZoneId));
    }

    // ---------- the basis is frozen and checked ----------

    [Fact]
    public void A_version_2_statement_rebuilt_from_its_stored_basis_is_identical_whatever_the_zone_is_later()
    {
        var c = FinancialDocumentV1Cases.All.First(x => x.Name == "statement-rich");
        var stored = c.Envelope with { AgingBasis = FinancialDocumentBuilder.PracticeBasis(c.Snapshot, c.CutOffs, Chicago) };
        var issued = FinancialDocumentBuilder.HashOf(FinancialDocumentBuilder.Statement(c.Snapshot, c.CutOffs, stored, 2));
        Assert.Equal(issued, FinancialDocumentBuilder.HashOf(FinancialDocumentBuilder.Statement(c.Snapshot, c.CutOffs, stored, 2)));   // the builder reads no zone: only the stored basis
        Assert.NotEqual(issued, FinancialDocumentBuilder.HashOf(V2(c.Snapshot, c.CutOffs, Tokyo, c.Envelope)));                        // a basis made in another zone is different content
    }

    [Fact]
    public void A_basis_that_does_not_belong_to_the_document_is_refused()
    {
        var s = new LedgerSnapshot([Charge(1, 10m, Utc(2030, 1, 2, 4, 42)), Charge(2, 5m, Utc(2030, 1, 3, 15, 0))], [], []);
        var cut = At(Utc(2030, 2, 1, 15, 0), entries: 2);
        var basis = FinancialDocumentBuilder.PracticeBasis(s, cut, Chicago);
        Assert.Throws<ArgumentException>(() => FinancialDocumentBuilder.AgingAt(s, cut with { AsOfUtc = cut.AsOfUtc.AddSeconds(1) }, basis));
        Assert.Throws<ArgumentException>(() => FinancialDocumentBuilder.AgingAt(s, cut, basis with { ServiceDays = basis.ServiceDays.Take(1).ToList() }));
        Assert.Throws<ArgumentException>(() => FinancialDocumentBuilder.AgingAt(s, cut, basis with { Policy = "UtcDays" }));
        Assert.Throws<ArgumentException>(() => FinancialDocumentBuilder.AgingAt(s, cut, basis with { AsOfLocalDate = "1 Feb 2030" }));
        Assert.Throws<ArgumentException>(() => FinancialDocumentBuilder.Statement(s, cut, Plain, 2));     // version 2 without a basis
    }

    // ---------- versions ----------

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void An_unknown_template_version_is_refused_for_statements_and_receipts(int version)
    {
        var c = FinancialDocumentV1Cases.All.First(x => x.Name == "receipt-plain");
        Assert.Equal(version, Assert.Throws<UnsupportedTemplateVersionException>(() => FinancialDocumentBuilder.Statement(c.Snapshot, c.CutOffs, c.Envelope, version)).Version);
        Assert.Throws<UnsupportedTemplateVersionException>(() => FinancialDocumentBuilder.Receipt(c.Snapshot, c.CutOffs, c.Envelope, 2, version));
        Assert.False(FinancialDocumentBuilder.IsSupported(version));
    }

    [Fact]
    public void Every_case_built_as_version_2_is_byte_for_byte_what_the_frozen_version_2_builder_made()
    {
        foreach (var c in FinancialDocumentV1Cases.All)
        {
            var stored = c.Envelope with { AgingBasis = OriginalFinancialDocumentBuilderV2.PracticeBasis(c.Snapshot, c.CutOffs, Chicago) };
            object now = c.Type == FinancialDocumentTypes.Statement ? FinancialDocumentBuilder.Statement(c.Snapshot, c.CutOffs, stored, 2) : FinancialDocumentBuilder.Receipt(c.Snapshot, c.CutOffs, stored, c.PaymentNumber!.Value, 2);
            object then = c.Type == FinancialDocumentTypes.Statement ? OriginalFinancialDocumentBuilderV2.Statement(c.Snapshot, c.CutOffs, stored, 2) : OriginalFinancialDocumentBuilderV2.Receipt(c.Snapshot, c.CutOffs, stored, c.PaymentNumber!.Value, 2);
            Assert.True(OriginalFinancialDocumentBuilderV2.CanonicalJson(then) == FinancialDocumentBuilder.CanonicalJson(now), c.Name);
            Assert.Equal(OriginalFinancialDocumentBuilderV2.HashOf(then), FinancialDocumentBuilder.HashOf(now));
        }
    }

    [Fact]
    public void Version_3_adds_only_the_reversed_by_numbers_and_the_adjustment_total()
    {
        var c = FinancialDocumentV1Cases.All.First(x => x.Name == "statement-rich");                     // charge 5 is reversed by entry 6; no adjustment
        var stored = c.Envelope with { AgingBasis = FinancialDocumentBuilder.PracticeBasis(c.Snapshot, c.CutOffs, Chicago) };
        var v2 = FinancialDocumentBuilder.Statement(c.Snapshot, c.CutOffs, stored, 2);
        var v3 = FinancialDocumentBuilder.Statement(c.Snapshot, c.CutOffs, stored, 3);
        Assert.Equal(6, v3.Lines.Single(l => l.Number == 5).ReversedByNumber);
        Assert.All(v3.Lines.Where(l => l.Number != 5), l => Assert.Null(l.ReversedByNumber));
        Assert.All(v2.Lines, l => Assert.Null(l.ReversedByNumber));
        Assert.Equal(("0.00", null), (v3.AdjustmentTotal, v2.AdjustmentTotal));
        Assert.Equal(FinancialDocumentBuilder.CanonicalJson(v2 with { TemplateVersion = 3, Lines = v3.Lines, AdjustmentTotal = "0.00" }), FinancialDocumentBuilder.CanonicalJson(v3));   // nothing else differs
        Assert.Matches("\r\n5,Charge,[^\r]*,6\r\n", FinancialDocumentBuilder.StatementCsv(v3, 1, Chicago));   // the CSV names the reversal of line 5
        Assert.Matches("\r\n5,Charge,[^\r]*,\r\n", FinancialDocumentBuilder.StatementCsv(v2, 1, Chicago));     // a version-2 line leaves it empty
        Assert.Equal(v2.Lines.Select(l => l with { ReversedByNumber = null }), v3.Lines.Select(l => l with { ReversedByNumber = null }));
        Assert.DoesNotContain("ReversedByNumber", FinancialDocumentBuilder.CanonicalJson(v2));
        Assert.DoesNotContain("AdjustmentTotal", FinancialDocumentBuilder.CanonicalJson(v2));

        var withAdjustment = new LedgerSnapshot([.. c.Snapshot.Entries, new SnapshotEntry(7, BillingKinds.Adjustment, -12.5m, null, null, null, null, null, null, "Courtesy", c.CutOffs.AsOfUtc, c.CutOffs.AsOfUtc),
            new SnapshotEntry(8, BillingKinds.Adjustment, 40m, null, null, null, null, null, null, "Opening balance", c.CutOffs.AsOfUtc, c.CutOffs.AsOfUtc),
            new SnapshotEntry(9, BillingKinds.Reversal, -40m, 8, null, null, null, null, null, "Entered twice", c.CutOffs.AsOfUtc, c.CutOffs.AsOfUtc)], c.Snapshot.Allocations, c.Snapshot.Releases);
        var cut = c.CutOffs with { EntryNumber = 9 };
        var adjusted = FinancialDocumentBuilder.Statement(withAdjustment, cut, c.Envelope with { AgingBasis = FinancialDocumentBuilder.PracticeBasis(withAdjustment, cut, Chicago) }, 3);
        decimal D(string x) => decimal.Parse(x, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("-12.50", adjusted.AdjustmentTotal);                                                  // the reversed adjustment no longer counts
        Assert.Equal(D(adjusted.Balance), D(adjusted.OpenChargeTotal) - D(adjusted.UnappliedTotal) + D(adjusted.AdjustmentTotal!));
    }

    [Fact]
    public void A_version_2_receipt_differs_from_version_1_only_in_the_version_it_carries()
    {
        var c = FinancialDocumentV1Cases.All.First(x => x.Name == "receipt-rich");
        var v1 = FinancialDocumentBuilder.Receipt(c.Snapshot, c.CutOffs, c.Envelope, 3, 1);
        var v2 = FinancialDocumentBuilder.Receipt(c.Snapshot, c.CutOffs, c.Envelope, 3, 2);
        Assert.Equal(v1 with { TemplateVersion = 2, Applications = v2.Applications }, v2);
        Assert.Equal(v1.Applications, v2.Applications);
    }
}
