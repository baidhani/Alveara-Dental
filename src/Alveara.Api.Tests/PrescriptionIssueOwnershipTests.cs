using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Prescribing;
using Alveara.Api.Architecture.Safety;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N026 S6 (issue-row ownership decision): a prescription issue carries no PatientId and is owned through its prescription. The database refuses any change or deletion of an issue (51305), and after an
/// approved merge of the parent the issue - untouched - resolves to the surviving patient through the prescription only.
/// </summary>
public class PrescriptionIssueOwnershipTests : TreatmentPlanTestBase
{
    private async Task<(Guid Prescription, Guid Issue)> IssuedForAnnAsync()
    {
        var saved = await WithDb(db => new PrescriptionService(db, Clock, new SafetyContextService(db, Clock)).RecordAsync(Ann, new PrescriptionInput("Ibuprofen", 400m, "mg", "Three times daily", "Reviewed with the patient.", $"rx-{Guid.NewGuid():N}"), S.Actor, default));
        var issue = new PrescriptionIssue
        {
            Id = Guid.NewGuid(), PrescriptionId = saved.Prescription.Id, IssueKind = PrescriptionIssueKinds.RecordCopy, IssuedAtUtc = DateTimeOffset.UtcNow, IssuedByUserId = S.Actor, TemplateVersion = "p2",
            StatusAtIssue = PrescriptionStatuses.Active, EnvelopeJson = "{}", IdempotencyKey = "own-1", CorrelationId = Guid.NewGuid(),
        };
        await WithDb(async db => { db.PrescriptionIssues.Add(issue); await db.SaveChangesAsync(); return 0; });
        return (saved.Prescription.Id, issue.Id);
    }

    private static int SqlNumber(Exception e) => (e as SqlException ?? e.InnerException as SqlException)?.Number ?? 0;

    [Fact]
    public async Task The_database_refuses_a_change_or_a_deletion_of_an_issue()
    {
        var (_, issue) = await IssuedForAnnAsync();
        await using var db = Fixture.CreateContext();
        var update = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE [PrescriptionIssues] SET [StatusAtIssue] = 'Cancelled' WHERE [Id] = @id", new SqlParameter("@id", issue)));
        var delete = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM [PrescriptionIssues] WHERE [Id] = @id", new SqlParameter("@id", issue)));
        Assert.Equal((51305, 51305), (SqlNumber(update), SqlNumber(delete)));
        Assert.Equal(PrescriptionStatuses.Active, (await db.PrescriptionIssues.AsNoTracking().SingleAsync(i => i.Id == issue)).StatusAtIssue);
    }

    [Fact]
    public async Task The_issue_table_has_no_patient_column_and_is_not_in_the_merge_registry()
    {
        await using var db = Fixture.CreateContext();
        var columns = await db.Database.SqlQueryRaw<string>("SELECT [name] AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID('PrescriptionIssues')").ToListAsync();
        Assert.DoesNotContain("PatientId", columns);
        Assert.Contains("PrescriptionId", columns);
        Assert.DoesNotContain(PatientOwnedData.All, c => c.Table == "PrescriptionIssues");
    }

    [Fact]
    public async Task After_an_approved_merge_of_the_parent_the_unchanged_issue_is_reached_only_through_the_survivors_prescription()
    {
        var (prescription, issue) = await IssuedForAnnAsync();
        var before = await WithDb(db => db.PrescriptionIssues.AsNoTracking().SingleAsync(i => i.Id == issue));

        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(Ann, Bo, default));
        Assert.True(preview.CanMerge, string.Join("; ", preview.Blockers.Select(b => b.Message)));
        await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(Ann, Bo, "Registered twice", preview.SourceRowVersion, preview.SurvivorRowVersion,
            preview.Conflicts.ToDictionary(c => c.Field, _ => "Survivor"), true, true), Guid.NewGuid().ToString("N"), default));

        var after = await WithDb(db => db.PrescriptionIssues.AsNoTracking().SingleAsync(i => i.Id == issue));
        Assert.Equal((before.PrescriptionId, before.EnvelopeJson, before.StatusAtIssue), (after.PrescriptionId, after.EnvelopeJson, after.StatusAtIssue));   // the issue row itself was not touched
        var (_, owner) = await WithDb(db => new PrescriptionIssueService(db, new PrescriptionRecordReader(db), Clock, NullLogger<PrescriptionIssueService>.Instance).GetAsync(issue, default));
        Assert.Equal(Bo, owner);
        Assert.Equal(Bo, await WithDb(db => db.Prescriptions.AsNoTracking().Where(p => p.Id == prescription).Select(p => p.PatientId).SingleAsync()));
        Assert.Equal(0, await WithDb(db => db.PrescriptionIssues.AsNoTracking().Join(db.Prescriptions, i => i.PrescriptionId, p => p.Id, (i, p) => p.PatientId).CountAsync(p => p == Ann)));
    }
}
