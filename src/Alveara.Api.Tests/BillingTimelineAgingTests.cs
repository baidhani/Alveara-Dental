using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C02 against real SQL Server: the timeline reads the whole history in the order it happened with each charge's completed procedure named; live aging uses the latest amendment of a completed
/// procedure's performed time while an issued document ages as it did on its own day (an amendment made after it never changes it, and its reprint still matches its hash).
/// </summary>
public class BillingTimelineAgingTests : CompletionTestBase
{
    private ProviderProfile _provider = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _provider = await ProviderAsync();
    }

    private async Task<(PlanView Plan, DateTimeOffset Performed)> CompletedAsync()
    {
        var plan = await AcceptedPlanAsync(2);
        var performed = DateTimeOffset.UtcNow.AddSeconds(2);
        await Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, [0, 1], AnnEncounter, _provider.Id, "set-1", performed), S.Actor, default));
        return (plan, performed);
    }

    private Task<DocumentPreview> Preview() => Bill(s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput("Statement", null), default));

    [Fact]
    public async Task Live_aging_follows_a_later_amendment_while_an_issued_statement_keeps_the_date_it_was_issued_with()
    {
        var (_, performed) = await CompletedAsync();
        var before = await Preview();
        Assert.Equal(2, before.Statement!.OpenCharges.Count);
        var originalDate = before.Statement.OpenCharges[0].ServiceDateUtc;
        Assert.Equal(FinancialDocumentBuilder.Instant(performed.AddTicks(-(performed.Ticks % TimeSpan.TicksPerMillisecond))), originalDate);
        var issued = await Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(before.PreviewToken, "doc-1"), S.Actor, default));

        Guid procedure;
        await using (var db = Fixture.CreateContext())
            procedure = await (from p in db.CompletedProcedures.AsNoTracking()
                               join e in db.BillingEntries.AsNoTracking() on p.PlanItemId equals e.PlanItemId
                               where e.EntryNumber == 1 select p.Id).SingleAsync();                    // the procedure behind charge 1 (a set finalizes both at one instant, so "the first" would be a coin toss)
        var amended = performed.AddSeconds(-1);                                                         // always a different instant from the original (never the clock, which can land on the same millisecond), and inside the time rules
        await Completion(s => s.AmendAsync(procedure, new AmendCompletedProcedureRequest("amend-1", 0, null, amended, null, "Performed earlier than recorded"), S.Actor, default));

        var live = await Bill(s => s.GetAgingAsync(Ann, default));
        var liveDates = live.OpenCharges.Select(c => c.ServiceDateUtc).ToList();
        Assert.NotEqual(originalDate, liveDates[0]);                                                    // live aging uses the amended time...
        Assert.Equal(before.Statement.OpenCharges[1].ServiceDateUtc, liveDates[1]);                     // ...of that procedure only
        Assert.Equal(FinancialDocumentBuilder.Instant(amended.AddTicks(-(amended.Ticks % TimeSpan.TicksPerMillisecond))), liveDates[0]);

        var reprint = await Bill(s => s.GetDocumentAsync(Ann, issued.Id, S.Actor, default));                      // the issued statement ages as it did on its own day, and still matches its hash
        Assert.Equal(originalDate, reprint.Statement!.OpenCharges[0].ServiceDateUtc);
        Assert.Equal(issued.ContentHash, FinancialDocumentBuilder.HashOf(reprint.Statement));

        var after = await Preview();                                                                    // a new preview sees the amendment
        Assert.Equal(liveDates[0], after.Statement!.OpenCharges[0].ServiceDateUtc);
        Assert.NotEqual(before.ContentHash, after.ContentHash);
    }

    [Fact]
    public async Task A_statement_issued_before_a_legacy_completion_was_adopted_still_reprints_exactly_as_issued_and_a_new_preview_sees_the_adoption()
    {
        // ALV-008-C02: adoption is the first operation that creates a completion record AFTER the charge it bills (a charge billing made by hand for a procedure completed the old way). An issued statement
        // aged that charge from when it was recorded, so a reprint must keep doing so: a record that did not exist when the statement was issued cannot change what it says.
        var plan = await AcceptedPlanAsync(1);
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        await Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Items[0].Id, "c-1"), S.Actor, default));
        var before = await Preview();
        var chargedDate = before.Statement!.OpenCharges.Single().ServiceDateUtc;                           // no completion record yet: it ages from when the charge was recorded
        var issued = await Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(before.PreviewToken, "doc-1"), S.Actor, default));

        var performed = DateTimeOffset.UtcNow.AddSeconds(2);
        var current = await PlanNowAsync(plan.Id);
        await Completion(s => s.AdoptAsync(plan.Id, plan.Items[0].Id, new AdoptCompletionRequest("adopt-1", current.RowVersion, AnnEncounter, _provider.Id, performed, "Completed before records existed"), S.Actor, default));

        var reprint = await Bill(s => s.GetDocumentAsync(Ann, issued.Id, S.Actor, default));              // refused as document_content_changed before the fix
        Assert.Equal(chargedDate, reprint.Statement!.OpenCharges.Single().ServiceDateUtc);
        Assert.Equal(issued.ContentHash, FinancialDocumentBuilder.HashOf(reprint.Statement));

        var after = await Preview();                                                                    // a new statement sees the adopted procedure's performed time
        Assert.Equal(FinancialDocumentBuilder.Instant(performed.AddTicks(-(performed.Ticks % TimeSpan.TicksPerMillisecond))), after.Statement!.OpenCharges.Single().ServiceDateUtc);
        Assert.NotEqual(before.ContentHash, after.ContentHash);
        var live = await Bill(s => s.GetAgingAsync(Ann, default));
        Assert.Equal(after.Statement.OpenCharges.Single().ServiceDateUtc, live.OpenCharges.Single().ServiceDateUtc);
    }

    [Fact]
    public async Task The_timeline_reads_the_history_in_order_and_names_the_procedure_behind_each_charge()
    {
        var (plan, _) = await CompletedAsync();
        var account = await Bill(s => s.GetAccountAsync(Ann, default));
        var paid = await Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(150m, "Cash", null, account.RowVersion, "p-1", [new(1, 60m), new(2, 40m)]), S.Actor, default));
        await Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(3, 80m, "Cash", "Patient request", paid.RowVersion, "rf-1"), S.Actor, default));

        var timeline = await Bill(s => s.GetTimelineAsync(Ann, default));
        Assert.Equal(["Charge", "Charge", "Payment", "Application", "Application", "Refund", "Release"], timeline.Select(e => e.Type));
        Assert.All(timeline.Where(e => e.Type == "Charge"), e => Assert.StartsWith("Completed procedure performed ", e.Source));
        Assert.Null(timeline.First(e => e.Type == "Payment").Source);
        Assert.Contains("refund", timeline[^1].Summary.ToLowerInvariant());
        await using (var db = Fixture.CreateContext())
        {
            var procedures = await db.CompletedProcedures.AsNoTracking().ToDictionaryAsync(p => p.PlanItemId, p => p.Id);
            var charges = timeline.Where(e => e.Type == "Charge").ToList();
            Assert.All(charges, c => Assert.Equal(procedures[c.PlanItemId!.Value], c.CompletedProcedureId));   // each charge links back to the completed procedure it bills
            Assert.All(timeline.Where(e => e.Type != "Charge"), e => Assert.Null(e.CompletedProcedureId));
        }
        Assert.Contains("Refund of payment 3", timeline[^2].Summary);
        Assert.Equal(timeline.OrderBy(e => e.AtUtc).Select(e => e.AtUtc), timeline.Select(e => e.AtUtc));
        Assert.NotNull(plan);
    }

    [Fact]
    public async Task An_account_with_nothing_on_it_has_an_empty_timeline_and_zero_aging()
    {
        Assert.Empty(await Bill(s => s.GetTimelineAsync(Ann, default)));
        var aging = await Bill(s => s.GetAgingAsync(Ann, default));
        Assert.Equal(("0.00", "0.00", "0.00", "0.00"), (aging.Buckets.Current, aging.Buckets.Days31To60, aging.Buckets.Days61To90, aging.Buckets.Over90));
        Assert.Empty(aging.OpenCharges);
    }
}
