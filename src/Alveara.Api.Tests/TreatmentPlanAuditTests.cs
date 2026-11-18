using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N023 (owner decision DR-7, finding F-192): treatment-plan audit rows name the action, the plan's reference code, the item number and the procedure code, who gave the answer and the
/// reason where one is required - and never the plan title (it can carry clinical text).
/// </summary>
public class TreatmentPlanAuditTests : TreatmentPlanTestBase
{
    private async Task<List<(string Type, string Details)>> AuditAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == nameof(TreatmentPlan)).OrderBy(a => a.TimestampUtc).Select(a => new { a.EventType, a.Details }).ToListAsync())
            .Select(a => (a.EventType, a.Details ?? "")).ToList();
    }

    [Fact]
    public async Task Answer_rename_and_withdraw_rows_name_codes_and_people_but_never_the_title()
    {
        const string title = "Implant for the upper left molar";
        var proc = await ProcedureAsync(code: "LOCAL-AUD01");
        var plan = await CreateAsync([Item(await DiagnosisAsync(), proc.Summary.Id)], title: title);
        plan = await Svc(s => s.SetReferenceCodeAsync(plan.Id, "plan-2030-01", plan.RowVersion, S.Actor, default));
        plan = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[0].Id, "Declined", "Too costly", "Ann Alder (patient)", plan.RowVersion, S.Actor, default));
        Assert.Equal("Ann Alder (patient)", plan.Items[0].DecidedByName);                                          // the authorized answer history keeps the entered name
        plan = await Svc(s => s.RenameAsync(plan.Id, "Plan B", "Patient asked", plan.RowVersion, S.Actor, default));
        await Svc(s => s.WithdrawAsync(plan.Id, "Replaced by plan B", plan.RowVersion, S.Actor, default));

        var rows = await AuditAsync();
        var declined = rows.Single(r => r.Type == "TreatmentPlanItemDeclined").Details;
        Assert.Contains("LOCAL-AUD01", declined);
        Assert.Contains("item 1", declined);
        Assert.Contains("plan-2030-01", declined);
        Assert.Contains("the person who gave the answer is recorded on the plan", declined);                          // owner addendum 01: no name in the audit
        Assert.All(rows, r => Assert.DoesNotContain("Ann", r.Details));
        Assert.Contains("plan-2030-01", rows.Single(r => r.Type == "TreatmentPlanRenamed").Details);
        Assert.Contains("plan-2030-01", rows.Single(r => r.Type == "TreatmentPlanWithdrawn").Details);
        Assert.All(rows, r => { Assert.DoesNotContain(title, r.Details); Assert.DoesNotContain("Plan B", r.Details); });
    }
}
