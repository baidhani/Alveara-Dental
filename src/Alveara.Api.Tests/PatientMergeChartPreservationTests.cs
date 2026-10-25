using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.ExternalCases;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Periodontal;
using Alveara.Api.Architecture.Prescribing;
using Alveara.Api.Architecture.Safety;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-017-C01, "preserving linked data" made concrete across the domains. The patient who is merged away has a real chart built through the real services (an appointment, a medical-history item and an allergy with its
/// reviewed list and a safety alert, a tooth finding, a finalized periodontal session, a diagnosis and a plan with a completed procedure, a charge and a payment on the account, a prescription, a document, a referral and a
/// follow-up task, a household and a person they are guarantor for) and the surviving patient has a smaller chart of their own. One merge later every row of every registered column is on the survivor and none is left
/// behind or lost, each record still agrees with the record it points at, the money is the same to the cent and the account still takes a payment, the services read the data for the survivor, the audit entries that
/// name the retired record are untouched and still resolve through the lineage, and the retired record stays in place.
/// </summary>
public class PatientMergeChartPreservationTests : MergeChartTestBase
{
    [Fact]
    public async Task One_merge_carries_a_whole_real_chart_across_the_domains_to_the_survivor_with_nothing_lost_and_everything_still_agreeing()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var domains = new[] { "Appointments", "ClinicalRecordItems", "SafetyAlerts", "ToothFindings", "PerioSessions", "Diagnoses", "TreatmentPlans", "CompletedProcedures", "BillingEntries", "Prescriptions", "Documents", "ReferralCases", "LabCases", "FollowUpTasks", "SafetyAlertAcknowledgements", "DiagnosisLinks" };
        foreach (var table in domains) { _ids[table] = await IdsAsync(table, Ann); Assert.True(_ids[table].Count > 0, $"the arrangement gave the retiring patient no {table}"); }   // every domain really has data
        var before = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));
        var account = await Bill(s => s.GetAccountAsync(Ann, default));
        Assert.True(account.HasAccount && account.BalanceOwed > 0m && account.Entries.Count >= 2);                                                    // charged and part paid
        var auditBefore = await WithDb(db => db.AuditLogEntries.AsNoTracking().Where(a => a.TargetUserAccountId == Ann && a.EventType != "PatientMerged").Select(a => a.Id).ToListAsync());
        Assert.NotEmpty(auditBefore);                                                                                                                 // the retired record is referred to by the audit log

        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(Ann, Bo, default));
        Assert.True(preview.CanMerge, string.Join("; ", preview.Blockers.Select(b => b.Message)));
        var result = await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(Ann, Bo, "Registered twice", preview.SourceRowVersion, preview.SurvivorRowVersion,
            preview.Conflicts.ToDictionary(c => c.Field, _ => "Survivor"), true, true), Guid.NewGuid().ToString("N"), default));

        // every registered column: nothing left on the retired patient, everything on the survivor, nothing lost or doubled
        var after = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));
        foreach (var column in PatientOwnedData.All.Where(c => c.Table != "PatientHistory"))
        {
            var k = $"{column.Table}.{column.Column}";
            Assert.True(after.Ann[k] == 0, $"{k}: {after.Ann[k]} rows are still the retired patient's");
            Assert.True(after.Bo[k] == before.Ann[k] + before.Bo[k], $"{k}: the survivor has {after.Bo[k]}, expected {before.Ann[k]} + {before.Bo[k]}");
        }
        Assert.Equal(PatientOwnedData.All.Count(c => before.Ann[$"{c.Table}.{c.Column}"] > 0), PatientOwnedData.All.Count(c => result.Moves.Any(m => m.Table == c.Table && m.Column == c.Column && m.Rows > 0)));
        Assert.True(PatientOwnedData.All.Count(c => before.Ann[$"{c.Table}.{c.Column}"] > 0) >= 34, "reached " + PatientOwnedData.All.Count(c => before.Ann[$"{c.Table}.{c.Column}"] > 0) + " of " + PatientOwnedData.All.Count + "; not reached: " + string.Join(", ", PatientOwnedData.All.Where(c => before.Ann[$"{c.Table}.{c.Column}"] == 0).Select(c => c.Table + "." + c.Column)));

        // each domain's own rows, by id, are the survivor's now; and every record still agrees with the one it points at
        foreach (var table in domains) Assert.Equal(_ids[table].Count, await OwnedByAsync(table, _ids[table], Bo));
        Assert.Equal(0, await AgreementViolationsAsync());

        // the money is the same to the cent, and the account still takes a payment, now for the survivor
        var moved = await Bill(s => s.GetAccountAsync(Bo, default));
        Assert.Equal((account.BalanceOwed, account.Entries.Count), (moved.BalanceOwed, moved.Entries.Count));
        var paidMore = await Bill(s => s.RecordPaymentAsync(Bo, PaymentOf(moved.BalanceOwed, moved.RowVersion!, key: "pay-after-merge", note: "Settled after the merge"), S.Actor, default));
        Assert.Equal(0m, paidMore.BalanceOwed);

        // the services read the data for the survivor
        Assert.Single(await WithDb(db => new PrescriptionService(db, Clock, new SafetyContextService(db, Clock)).ListAsync(Bo, default)));
        Assert.True((await WithDb(db => new ExternalCaseService(db, PatientTestSupport.Clock).PatientTimelineAsync(Bo, default))).Count > 0);
        var plans = await WithDb(db => db.TreatmentPlans.AsNoTracking().Where(p => p.PatientId == Bo).ToListAsync());
        Assert.True(plans.Count >= 2);
        Assert.Equal(2, await WithDb(db => db.Appointments.CountAsync(a => a.PatientId == Bo)));                                                      // the survivor's own and the retired patient's
        Assert.Contains("Penicillin", JsonString(await ClinicalRecordOfAsync(Bo))); Assert.Contains("Codeine", JsonString(await ClinicalRecordOfAsync(Bo)));

        // household and guarantor follow the survivor
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(Bo, (await db.Patients.AsNoTracking().SingleAsync(p => p.Id == _ids["dependant"][0])).GuarantorPatientId);
            Assert.NotNull((await db.Patients.AsNoTracking().SingleAsync(p => p.Id == Bo)).HouseholdId);
        }

        // the audit entries that name the retired record are untouched and still resolve through the lineage; the retired record stays in place
        await using (var db = Fixture.CreateContext())
        {
            var still = await db.AuditLogEntries.AsNoTracking().Where(a => a.TargetUserAccountId == Ann && a.EventType != "PatientMerged").Select(a => a.Id).ToListAsync();
            Assert.Equal(auditBefore.Order().ToArray(), still.Order().ToArray());
            var retired = await db.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann);
            Assert.False(retired.IsActive); Assert.NotNull(retired.MergedAtUtc);
        }
        var history = await WithDb(db => new PatientMergeService(db, Clock).HistoryAsync(Bo, default));
        Assert.Equal(Ann, Assert.Single(history.Absorbed).PatientId);
        Assert.Equal(Bo, (await WithDb(db => new PatientMergeService(db, Clock).HistoryAsync(Ann, default))).MergedInto!.PatientId);
        Assert.Equal(PatientOwnedData.All.Count, result.Moves.Count);
    }

    private Task<object> ClinicalRecordOfAsync(Guid patient) => WithDb<object>(async db => await new Alveara.Api.Architecture.Clinical.ClinicalRecordReader(db).GetAsync(patient, default));

    private static string JsonString(object o) => System.Text.Json.JsonSerializer.Serialize(o);
}
