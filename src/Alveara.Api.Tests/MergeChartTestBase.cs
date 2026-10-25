using Microsoft.Data.SqlClient;
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
/// ALV-017-C01: the arrangement the merge's cross-domain tests share: a real chart for the patient who is merged away (built through the real services across the domains) and a smaller one for the survivor, plus the
/// counting helpers that prove nothing was lost or left behind.
/// </summary>
public abstract class MergeChartTestBase : CompletionTestBase
{
    protected readonly Dictionary<string, List<Guid>> _ids = new();

    protected async Task<Dictionary<string, int>> CountsAsync(Guid patient)
    {
        await using var db = Fixture.CreateContext();
        var counts = new Dictionary<string, int>();
        foreach (var c in PatientOwnedData.All)
            counts[$"{c.Table}.{c.Column}"] = await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM {PatientMergeCatalog.Q(c.Table)} WHERE {PatientMergeCatalog.Q(c.Column)} = @p", new SqlParameter("@p", patient)).SingleAsync();
        return counts;
    }

    protected async Task<List<Guid>> IdsAsync(string table, Guid patient)
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.SqlQueryRaw<Guid>($"SELECT [Id] AS [Value] FROM {PatientMergeCatalog.Q(table)} WHERE [PatientId] = @p", new SqlParameter("@p", patient)).ToListAsync();
    }

    protected async Task<int> OwnedByAsync(string table, IEnumerable<Guid> ids, Guid patient)
    {
        var list = ids.ToList(); if (list.Count == 0) return 0;
        await using var db = Fixture.CreateContext();
        return await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM {PatientMergeCatalog.Q(table)} WHERE [PatientId] = @p AND [Id] IN ({string.Join(",", list.Select(i => $"'{i}'"))})", new SqlParameter("@p", patient)).SingleAsync();
    }

    protected async Task GiveRetiringChartAsync()
    {
        await S.ScheduleAsync(S.Request(Ann, new DateTime(2027, 9, 14, 9, 0, 0)));                                                                    // an appointment
        await RecordItemAsync("MedicalHistory", "Atrial fibrillation", patient: Ann);                                                                // history, an allergy, the reviewed list and a safety alert
        await RecordItemAsync("Allergy", "Penicillin", "Hives", "Severe", patient: Ann);
        await RecordReviewAsync("Allergy", "Reviewed", Ann);
        await AddAlertAsync("Latex allergy", patient: Ann);
        var alert = (await WithDb(db => db.SafetyAlerts.AsNoTracking().SingleAsync(x => x.PatientId == Ann && x.Title == "Latex allergy")));
        await AlertsAsync(s => s.AcknowledgeAsync(alert.Id, alert.Revision, S.Actor, default));                                                                    // acknowledged by the dentist
        var finding = await FindingAsync("26", "O", patient: Ann);                                                                                    // the chart: a tooth finding, and a diagnosis linked to it
        await LinkAsync(await DiagnosisAsync(Ann, "Caries on 26"), finding);
        PerioReadingInput[] Tooth(string tooth) => [.. PerioRules.Sites.Select(s => new PerioReadingInput(tooth, s, 3, 1, false, null, null))];
        var perio = await WithDb(db => new PerioSessionService(db, Clock).StartAsync(Ann, S.Actor, default));                                       // a finalized periodontal session
        perio = await WithDb(db => new PerioSessionService(db, Clock).SaveEntriesAsync(perio.Id, perio.RowVersion, new PerioEntryBatch(Tooth("16"), [new PerioToothInput("16", 1, null, false)], null, null), S.Actor, default));
        var open = perio;
        await WithDb(db => new PerioSessionService(db, Clock).FinalizeAsync(open.Id, open.RowVersion, S.Actor, default));
        var provider = await ProviderAsync();                                                                                                         // a diagnosis, a plan and a procedure really completed
        var accepted = await AcceptedPlanAsync(2, Ann, 700);
        await Completion(s => s.CompleteAsync(accepted.Id, RequestFor(accepted, [0], AnnEncounter, provider.Id, "set-1", DateTimeOffset.UtcNow.AddSeconds(2)), S.Actor, default));
        var billable = await BillablePlanAsync(Ann);                                                                                                  // a second plan, charged and part paid
        var charged = await Bill(s => s.RecordChargeAsync(Ann, ChargeOf(billable.Completed), S.Actor, default));
        await Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(60m, charged.RowVersion!, note: "Cash at the desk"), S.Actor, default));
        await WithDb(db => new PrescriptionService(db, Clock, new SafetyContextService(db, Clock)).RecordAsync(Ann, new PrescriptionInput("Ibuprofen", 400m, "mg", "Three times daily", null, $"rx-{Guid.NewGuid():N}"), S.Actor, default));
        await using (var db = Fixture.CreateContext())
        {
            db.Add(new PatientDocument
            {
                Id = Guid.NewGuid(), PatientId = Ann, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), Description = "Panoramic film.", OriginalFileName = "pano.png", DetectedType = "png",
                ContentType = "image/png", SizeBytes = 2048, Sha256 = new string('a', 64), BlobId = Guid.NewGuid(), ImportedByUserId = S.Actor, ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = new string('b', 64),
            });
            await db.SaveChangesAsync();
        }
        var party = await WithDb(db => new ExternalPartyService(db, PatientTestSupport.Clock).CreateAsync(S.Actor, new CreatePartyRequest(ExternalPartyKinds.ReferralProvider, $"Party {Guid.NewGuid():N}", null), default));
        await WithDb(db => new ExternalCaseService(db, PatientTestSupport.Clock).CreateReferralAsync(S.Actor, Ann, new CreateReferralRequest(party.Id, "Second opinion on tooth 16", null, null, null), Guid.NewGuid().ToString("N"), default));
        var lab = await WithDb(db => new ExternalPartyService(db, PatientTestSupport.Clock).CreateAsync(S.Actor, new CreatePartyRequest(ExternalPartyKinds.LabVendor, $"Lab {Guid.NewGuid():N}", null), default));
        await WithDb(db => new ExternalCaseService(db, PatientTestSupport.Clock).CreateLabAsync(S.Actor, Ann, new CreateLabRequest(lab.Id, "Crown, zirconia", null, null, null, null, null, null), Guid.NewGuid().ToString("N"), default));
        await using (var db = Fixture.CreateContext()) { db.PatientRecallPlans.Add(new Alveara.Api.Architecture.Recall.PatientRecallPlan { PatientId = Ann, IntervalDays = 120, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedByUserId = S.Actor }); await db.SaveChangesAsync(); }
        await WithDb(db => new FollowUpService(db, new TestClock(DateTimeOffset.UtcNow), PolicyTestSupport.Defaults()).CreateAsync(S.Actor, Ann, new CreateFollowUpRequest("Call about the crown", "Private note", DateTimeOffset.UtcNow.AddDays(3), Other, null), "k-open", default));
        var dependant = await S.PatientAsync("Eli", "Lee");                                                                                           // household and guarantor
        await using (var db = Fixture.CreateContext())
        {
            var household = new Household { Id = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow };
            db.Households.Add(household); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE [Patients] SET [HouseholdId] = {0}, [HouseholdRelationship] = 'Parent' WHERE [Id] = {1}; UPDATE [Patients] SET [GuarantorPatientId] = {1} WHERE [Id] = {2}", household.Id, Ann, dependant);
        }
        _ids["dependant"] = [dependant];
    }

    protected async Task GiveSurvivingChartAsync()
    {
        await S.ScheduleAsync(S.Request(Bo, new DateTime(2027, 9, 15, 9, 0, 0)));
        await RecordItemAsync("Allergy", "Codeine", "Nausea", "Mild", patient: Bo);
        await FindingAsync("36", "O", patient: Bo);
    }

    protected async Task<int> AgreementViolationsAsync()
    {
        await using var db = Fixture.CreateContext();
        var total = 0;
        foreach (var edge in new PatientMergeCatalog(db.Model).Edges)
        {
            var join = string.Join(" AND ", edge.ChildColumns.Zip(edge.ParentColumns, (c, p) => $"c.{PatientMergeCatalog.Q(c)} = p.{PatientMergeCatalog.Q(p)}"));
            total += await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM {PatientMergeCatalog.Q(edge.Child)} c JOIN {PatientMergeCatalog.Q(edge.Parent)} p ON {join} WHERE c.[PatientId] <> p.[PatientId]").SingleAsync();
        }
        return total;
    }
}
