using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.ExternalCases;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C02, the outside parties, referral cases, lab cases, their history and their document links on their own: rows written straight to the database, around any service. A party has a kind from a closed set, a plain
/// name that is unique within its kind, and is never deleted (51220); a case has a status from its own set and the dates, closure and reason that status needs, names a party of the matching kind that is active when the
/// case is made, keeps its patient and its party once sent, is never deleted and never changes once completed or cancelled (51221), and refers only to an encounter, plan item or document of its own patient (51222);
/// the history is append-only (51223); a follow-up task chases a case of its own patient and keeps that link (51224, 51210); and the migration rolls back and forward while no case data exists and refuses to
/// roll back over it (51225).
/// </summary>
public class ExternalCaseSchemaTests : TreatmentPlanTestBase
{
    private const string BeforeExternalCases = "20261024060516_AddWorkQueue";

    private async Task AddAsync(params object[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private Task<Microsoft.Data.SqlClient.SqlException> AddRefusedAsync(params object[] rows) => Refusal(() => AddAsync(rows));

    private async Task<Microsoft.Data.SqlClient.SqlException> SqlRefusalAsync(FormattableString sql)
    {
        await using var db = Fixture.CreateContext();
        return await Refusal(() => db.Database.ExecuteSqlAsync(sql));
    }

    private async Task<int> CountAsync<T>() where T : class { await using var db = Fixture.CreateContext(); return await db.Set<T>().CountAsync(); }

    private static readonly DateOnly Today = new(2026, 10, 24);

    private ExternalParty Party(string kind = ExternalPartyKinds.ReferralProvider, Action<ExternalParty>? change = null)
    {
        var p = new ExternalParty { Id = Guid.NewGuid(), Kind = kind, Name = $"Party {Guid.NewGuid():N}", ContactDetails = "555-010-0100", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor, UpdatedAtUtc = DateTimeOffset.UtcNow };
        change?.Invoke(p);
        return p;
    }

    private ReferralCase Referral(Guid party, Action<ReferralCase>? change = null, Guid? patient = null)
    {
        var c = new ReferralCase
        {
            Id = Guid.NewGuid(), PatientId = patient ?? Ann, PartyId = party, Reason = "Evaluation of an impacted wisdom tooth", Status = ExternalCaseRules.Draft, IdempotencyKey = $"ref-{Guid.NewGuid():N}",
            CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor, UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        change?.Invoke(c);
        return c;
    }

    private LabCase Lab(Guid party, Action<LabCase>? change = null, Guid? patient = null)
    {
        var c = new LabCase
        {
            Id = Guid.NewGuid(), PatientId = patient ?? Ann, PartyId = party, Title = "Crown, zirconia", Status = ExternalCaseRules.Draft, IdempotencyKey = $"lab-{Guid.NewGuid():N}",
            CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor, UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        change?.Invoke(c);
        return c;
    }

    private static void Sent(ReferralCase c) { c.Status = ExternalCaseRules.Sent; c.SentOn = Today.AddDays(-3); }
    private static void Sent(LabCase c) { c.Status = ExternalCaseRules.Sent; c.SentOn = Today.AddDays(-3); }

    private async Task<(ExternalParty provider, ExternalParty vendor)> PartiesAsync()
    {
        var p = Party(); var v = Party(ExternalPartyKinds.LabVendor);
        await AddAsync(p, v);
        return (p, v);
    }

    private PatientDocument Document(Guid patient) => new()
    {
        Id = Guid.NewGuid(), PatientId = patient, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), Description = "Panoramic film.", OriginalFileName = "pano.png", DetectedType = "png",
        ContentType = "image/png", SizeBytes = 2048, Sha256 = new string('a', 64), BlobId = Guid.NewGuid(), ImportedByUserId = S.Actor, ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = new string('a', 64),
    };

    private async Task<TreatmentPlanItem> PlanItemAsync(Guid patient)
    {
        var plan = new TreatmentPlan { Id = Guid.NewGuid(), PatientId = patient, IdempotencyKey = $"plan-{Guid.NewGuid():N}", Title = "Plan", Status = TreatmentPlanStatuses.Proposed, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };
        var procedure = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        var item = new TreatmentPlanItem
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = patient, ItemNumber = 1, IdempotencyKey = "item-1", DiagnosisId = await DiagnosisAsync(patient), ProcedureId = procedure.Summary.Id,
            ProcedureVersionId = procedure.Versions[0].VersionId, Fee = procedure.Versions[0].Fee, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor,
        };
        await AddAsync(plan, item);
        return item;
    }

    // ---------- outside parties ----------

    [Fact]
    public async Task A_party_has_a_kind_from_the_closed_set_a_plain_name_and_a_name_that_is_unique_within_its_kind()
    {
        var p = Party(); await AddAsync(p);
        Assert.Equal(547, (await AddRefusedAsync(Party("Dentist"))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Party(change: x => x.Name = "   "))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Party(change: x => x.Name = "bell\u0007"))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Party(change: x => x.ContactDetails = "  "))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Party(change: x => x.UpdatedAtUtc = x.CreatedAtUtc.AddSeconds(-1)))).Number);
        Assert.NotEqual(0, (await AddRefusedAsync(Party(change: x => x.Name = new string('n', 201)))).Number);
        Assert.True((await AddRefusedAsync(Party(change: x => x.Name = p.Name.ToUpperInvariant()))).Number is 2601 or 2627);        // the same name in other letters is the same name
        await AddAsync(Party(ExternalPartyKinds.LabVendor, x => x.Name = p.Name));                                                  // but a lab vendor may share a referral provider's name
        Assert.Equal(2, await CountAsync<ExternalParty>());
    }

    [Fact]
    public async Task A_party_is_never_deleted_and_never_changes_its_kind_creator_or_creation_time_but_can_be_made_inactive()
    {
        var p = Party(); await AddAsync(p);
        Assert.Equal(51220, (await SqlRefusalAsync($"DELETE FROM [ExternalParties] WHERE [Id] = {p.Id}")).Number);
        Assert.Equal(51220, (await SqlRefusalAsync($"UPDATE [ExternalParties] SET [Kind] = N'LabVendor' WHERE [Id] = {p.Id}")).Number);
        Assert.Equal(51220, (await SqlRefusalAsync($"UPDATE [ExternalParties] SET [CreatedAtUtc] = DATEADD(DAY, -1, [CreatedAtUtc]) WHERE [Id] = {p.Id}")).Number);
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlAsync($"UPDATE [ExternalParties] SET [IsActive] = 0, [UpdatedAtUtc] = DATEADD(MINUTE, 1, [UpdatedAtUtc]) WHERE [Id] = {p.Id}");
        Assert.False((await db.ExternalParties.AsNoTracking().SingleAsync()).IsActive);
    }

    // ---------- the shape each status needs ----------

    [Fact]
    public async Task A_referral_in_each_status_has_exactly_the_dates_and_closure_that_status_needs()
    {
        var (p, _) = await PartiesAsync();
        var closed = DateTimeOffset.UtcNow.AddMinutes(5);
        await AddAsync(Referral(p.Id));                                                                                                             // Draft
        await AddAsync(Referral(p.Id, c => { Sent(c); }));                                                                                          // Sent
        await AddAsync(Referral(p.Id, c => { Sent(c); c.Status = "Received"; c.ReceivedOn = Today.AddDays(-1); }));                                 // Received
        await AddAsync(Referral(p.Id, c => { Sent(c); c.Status = "Completed"; c.ReceivedOn = Today.AddDays(-1); c.ClosedAtUtc = closed; c.ClosedByUserId = S.Actor; }));
        await AddAsync(Referral(p.Id, c => { c.Status = "Cancelled"; c.ClosedAtUtc = closed; c.ClosedByUserId = S.Actor; c.ClosureNote = "Patient declined"; }));
        Assert.Equal(5, await CountAsync<ReferralCase>());

        // every wrong shape is refused by a check, whichever status it is dressed as
        var wrong = new Action<ReferralCase>[]
        {
            c => c.SentOn = Today,                                                                                                                  // a draft has not been sent
            c => c.Status = "Sent",                                                                                                                 // sent without a sent date
            c => { c.Status = "Sent"; c.SentOn = Today; c.ReceivedOn = Today; },                                                                    // sent but already received
            c => { c.Status = "Received"; c.SentOn = Today; },                                                                                      // received without a received date
            c => { Sent(c); c.Status = "Completed"; c.ReceivedOn = Today; },                                                                        // completed without who and when
            c => { c.Status = "Cancelled"; c.ClosedAtUtc = closed; c.ClosedByUserId = S.Actor; },                                                   // cancelled without a reason
            c => { Sent(c); c.ClosedAtUtc = closed; },                                                                                              // open but closed
            c => c.Status = "Done",                                                                                                                 // unknown status
            c => c.Status = "InProgress",                                                                                                           // a lab status on a referral
            c => { Sent(c); c.ReceivedOn = Today.AddDays(-30); c.Status = "Received"; },                                                            // received before it was sent
            c => { c.ReceivedOn = Today; },                                                                                                         // received without ever being sent
            c => { Sent(c); c.SentOn = new DateOnly(1999, 12, 31); },                                                                               // before 2000
            c => { Sent(c); c.DueOn = new DateOnly(2200, 1, 1); },                                                                                  // absurd due date
        };
        foreach (var change in wrong) Assert.Equal(547, (await AddRefusedAsync(Referral(p.Id, change))).Number);
        Assert.Equal(5, await CountAsync<ReferralCase>());
    }

    [Fact]
    public async Task A_lab_case_may_also_be_in_progress_and_has_the_same_shapes()
    {
        var (_, v) = await PartiesAsync();
        await AddAsync(Lab(v.Id, c => { Sent(c); c.Status = "InProgress"; }));
        Assert.Equal(547, (await AddRefusedAsync(Lab(v.Id, c => c.Status = "InProgress"))).Number);                                                 // in progress without a sent date
        Assert.Equal(547, (await AddRefusedAsync(Lab(v.Id, c => c.Status = "Unknown"))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Lab(v.Id, c => { Sent(c); c.Status = "InProgress"; c.ReceivedOn = Today; }))).Number);
        Assert.Equal(1, await CountAsync<LabCase>());
    }

    [Fact]
    public async Task A_case_needs_a_plain_reason_or_title_and_plain_notes_within_their_limits()
    {
        var (p, v) = await PartiesAsync();
        Assert.Equal(547, (await AddRefusedAsync(Referral(p.Id, c => c.Reason = "   "))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Referral(p.Id, c => c.Reason = "bell\u0007"))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Referral(p.Id, c => c.Notes = "  "))).Number);
        Assert.NotEqual(0, (await AddRefusedAsync(Referral(p.Id, c => c.Reason = new string('r', 501)))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Lab(v.Id, c => c.Title = " "))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Lab(v.Id, c => c.Shade = " "))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Lab(v.Id, c => c.Material = "x\u0001"))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Lab(v.Id, c => c.ToothKey = "99"))).Number);
        Assert.NotEqual(0, (await AddRefusedAsync(Lab(v.Id, c => c.Shade = new string('s', 51)))).Number);
        await AddAsync(Lab(v.Id, c => { c.ToothKey = "36"; c.Material = "Zirconia"; c.Shade = "A2"; }));
        Assert.Equal(1, await CountAsync<LabCase>());
    }

    [Fact]
    public async Task A_create_with_the_same_key_for_the_same_patient_cannot_make_a_second_case()
    {
        var (p, v) = await PartiesAsync();
        await AddAsync(Referral(p.Id, c => c.IdempotencyKey = "same"), Lab(v.Id, c => c.IdempotencyKey = "same"));
        Assert.True((await AddRefusedAsync(Referral(p.Id, c => c.IdempotencyKey = "same"))).Number is 2601 or 2627);
        Assert.True((await AddRefusedAsync(Lab(v.Id, c => c.IdempotencyKey = "same"))).Number is 2601 or 2627);
        await AddAsync(Referral(p.Id, c => c.IdempotencyKey = "same", patient: Bo));                                                                  // another patient may use the same key
        Assert.Equal(2, await CountAsync<ReferralCase>());
    }

    // ---------- the party, the encounter, the plan item ----------

    [Fact]
    public async Task A_referral_goes_to_a_referral_provider_and_a_lab_case_to_a_lab_vendor_and_the_party_is_active_when_the_case_is_made()
    {
        var (p, v) = await PartiesAsync();
        Assert.Equal(51222, (await AddRefusedAsync(Referral(v.Id))).Number);
        Assert.Equal(51222, (await AddRefusedAsync(Lab(p.Id))).Number);
        var retired = Party(change: x => x.IsActive = false); await AddAsync(retired);
        Assert.Equal(51222, (await AddRefusedAsync(Referral(retired.Id))).Number);
        // a case already made keeps working after its party is made inactive
        var live = Referral(p.Id, c => Sent(c)); await AddAsync(live);
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlAsync($"UPDATE [ExternalParties] SET [IsActive] = 0, [UpdatedAtUtc] = DATEADD(MINUTE, 1, [UpdatedAtUtc]) WHERE [Id] = {p.Id}");
        await db.Database.ExecuteSqlAsync($"UPDATE [ReferralCases] SET [Status] = N'Received', [ReceivedOn] = {Today}, [UpdatedAtUtc] = DATEADD(MINUTE, 1, [UpdatedAtUtc]) WHERE [Id] = {live.Id}");
        Assert.Equal("Received", (await db.ReferralCases.AsNoTracking().SingleAsync(c => c.Id == live.Id)).Status);
    }

    [Fact]
    public async Task A_case_keeps_its_party_once_it_has_been_sent_but_a_draft_may_be_redirected_to_an_active_party_of_the_same_kind()
    {
        var (p, v) = await PartiesAsync();
        var other = Party(); await AddAsync(other);
        var draft = Referral(p.Id); var sent = Referral(p.Id, c => Sent(c)); await AddAsync(draft, sent);
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlAsync($"UPDATE [ReferralCases] SET [PartyId] = {other.Id} WHERE [Id] = {draft.Id}");
        Assert.Equal(51221, (await SqlRefusalAsync($"UPDATE [ReferralCases] SET [PartyId] = {other.Id} WHERE [Id] = {sent.Id}")).Number);
        Assert.Equal(51222, (await SqlRefusalAsync($"UPDATE [ReferralCases] SET [PartyId] = {v.Id} WHERE [Id] = {draft.Id}")).Number);                  // but not to a party of the other kind
    }

    [Fact]
    public async Task A_referral_names_an_encounter_of_its_own_patient_and_a_lab_case_a_plan_item_of_its_own_patient()
    {
        var (p, v) = await PartiesAsync();
        await AddAsync(Referral(p.Id, c => c.EncounterId = AnnEncounter));
        Assert.Equal(51222, (await AddRefusedAsync(Referral(p.Id, c => c.EncounterId = BoEncounter))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Referral(p.Id, c => c.EncounterId = Guid.NewGuid()))).Number);                                  // an encounter that does not exist
        var annItem = await PlanItemAsync(Ann); var boItem = await PlanItemAsync(Bo);
        await AddAsync(Lab(v.Id, c => c.TreatmentPlanItemId = annItem.Id));
        Assert.Equal(51222, (await AddRefusedAsync(Lab(v.Id, c => c.TreatmentPlanItemId = boItem.Id))).Number);
    }

    // ---------- a case is final ----------

    [Fact]
    public async Task A_case_is_never_deleted_a_finished_case_never_changes_and_a_case_keeps_its_patient_creator_time_and_key()
    {
        var (p, v) = await PartiesAsync();
        var open = Referral(p.Id, c => Sent(c));
        var done = Lab(v.Id, c => { c.Status = "Cancelled"; c.ClosedAtUtc = DateTimeOffset.UtcNow.AddMinutes(5); c.ClosedByUserId = S.Actor; c.ClosureNote = "Remade elsewhere"; });
        await AddAsync(open, done);
        Assert.Equal(51221, (await SqlRefusalAsync($"DELETE FROM [ReferralCases] WHERE [Id] = {open.Id}")).Number);
        Assert.Equal(51221, (await SqlRefusalAsync($"DELETE FROM [LabCases] WHERE [Id] = {done.Id}")).Number);
        Assert.Equal(51221, (await SqlRefusalAsync($"UPDATE [LabCases] SET [Notes] = N'late edit' WHERE [Id] = {done.Id}")).Number);
        Assert.Equal(51221, (await SqlRefusalAsync($"UPDATE [ReferralCases] SET [PatientId] = {Bo} WHERE [Id] = {open.Id}")).Number);
        Assert.Equal(51221, (await SqlRefusalAsync($"UPDATE [ReferralCases] SET [CreatedByUserId] = {Other} WHERE [Id] = {open.Id}")).Number);
        Assert.Equal(51221, (await SqlRefusalAsync($"UPDATE [ReferralCases] SET [IdempotencyKey] = N'other' WHERE [Id] = {open.Id}")).Number);
        Assert.Equal(51221, (await SqlRefusalAsync($"UPDATE [ReferralCases] SET [CreatedAtUtc] = DATEADD(MINUTE, -1, [CreatedAtUtc]) WHERE [Id] = {open.Id}")).Number);
    }

    // ---------- the history ----------

    [Fact]
    public async Task A_history_entry_belongs_to_exactly_one_case_has_the_shape_of_its_kind_and_is_never_changed_or_deleted()
    {
        var (p, v) = await PartiesAsync();
        var r = Referral(p.Id); var l = Lab(v.Id); await AddAsync(r, l);
        ExternalCaseEvent Ev(Action<ExternalCaseEvent>? change = null) { var e = new ExternalCaseEvent { Id = Guid.NewGuid(), ReferralCaseId = r.Id, Kind = "Created", ToStatus = "Draft", ActorUserId = S.Actor, OccurredAtUtc = DateTimeOffset.UtcNow }; change?.Invoke(e); return e; }
        await AddAsync(Ev(), Ev(e => { e.ReferralCaseId = null; e.LabCaseId = l.Id; }));
        await AddAsync(Ev(e => { e.Kind = "StatusChanged"; e.FromStatus = "Draft"; e.ToStatus = "Sent"; e.Note = "Sent by post"; }));
        Assert.Equal(547, (await AddRefusedAsync(Ev(e => e.LabCaseId = l.Id))).Number);                                                             // both cases
        Assert.Equal(547, (await AddRefusedAsync(Ev(e => e.ReferralCaseId = null))).Number);                                                        // neither
        Assert.Equal(547, (await AddRefusedAsync(Ev(e => e.Kind = "Deleted"))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Ev(e => { e.Kind = "StatusChanged"; e.ToStatus = null; }))).Number);                               // a status change without its new status
        Assert.Equal(547, (await AddRefusedAsync(Ev(e => { e.Kind = "DocumentLinked"; e.ToStatus = null; }))).Number);                              // a document entry without a document
        Assert.Equal(547, (await AddRefusedAsync(Ev(e => e.Note = "  "))).Number);
        Assert.Equal(51223, (await SqlRefusalAsync($"UPDATE [ExternalCaseEvents] SET [Note] = N'x'")).Number);
        Assert.Equal(51223, (await SqlRefusalAsync($"DELETE FROM [ExternalCaseEvents]")).Number);
        Assert.Equal(3, await CountAsync<ExternalCaseEvent>());
    }

    // ---------- document links ----------

    [Fact]
    public async Task A_document_is_linked_not_copied_once_per_case_and_only_to_a_case_of_the_documents_own_patient()
    {
        var (p, v) = await PartiesAsync();
        var r = Referral(p.Id); var l = Lab(v.Id); var annDoc = Document(Ann); var boDoc = Document(Bo); var spare = Document(Ann);
        await AddAsync(r, l, annDoc, boDoc, spare);
        ExternalCaseDocument Link(Guid doc, Guid? referral, Guid? lab) => new() { Id = Guid.NewGuid(), ReferralCaseId = referral, LabCaseId = lab, DocumentId = doc, LinkedByUserId = S.Actor, LinkedAtUtc = DateTimeOffset.UtcNow };
        await AddAsync(Link(annDoc.Id, r.Id, null), Link(annDoc.Id, null, l.Id));                                                                    // the same document in two cases
        Assert.True((await AddRefusedAsync(Link(annDoc.Id, r.Id, null))).Number is 2601 or 2627);                                                  // but only once in one case
        Assert.True((await AddRefusedAsync(Link(annDoc.Id, null, l.Id))).Number is 2601 or 2627);
        Assert.Equal(51222, (await AddRefusedAsync(Link(boDoc.Id, r.Id, null))).Number);                                                            // another patient's document
        Assert.Equal(547, (await AddRefusedAsync(Link(spare.Id, r.Id, l.Id))).Number);                                                             // both cases
        Assert.Equal(547, (await AddRefusedAsync(Link(spare.Id, null, null))).Number);                                                             // neither case
        Assert.Equal(2, await CountAsync<ExternalCaseDocument>());
        Assert.Equal(3, await CountAsync<PatientDocument>());                                                                                       // nothing was copied
        await SqlSucceedsAsync($"DELETE FROM [ExternalCaseDocuments] WHERE [LabCaseId] = {l.Id}");                                          // unlinking is allowed (the history keeps the fact)
        Assert.Equal(1, await CountAsync<ExternalCaseDocument>());
    }

    private async Task SqlSucceedsAsync(FormattableString sql) { await using var db = Fixture.CreateContext(); await db.Database.ExecuteSqlAsync(sql); }

    // ---------- the follow-up task that chases a case ----------

    private FollowUpTask Chase(string kind, Guid? referral, Guid? lab, Guid patient, Action<FollowUpTask>? change = null)
    {
        var t = new FollowUpTask
        {
            Id = Guid.NewGuid(), PatientId = patient, AssigneeUserId = null, Title = "Chase the case", DueAtUtc = DateTimeOffset.UtcNow.AddDays(1), Status = FollowUpStatuses.Open, Kind = kind, CreatedByUserId = null,
            CreatedAtUtc = DateTimeOffset.UtcNow, SourceKey = $"{(kind == FollowUpKinds.ReferralFollowUp ? "referral" : "lab")}:{Guid.NewGuid():N}:20261101", SourceReferralCaseId = referral, SourceLabCaseId = lab,
        };
        change?.Invoke(t);
        return t;
    }

    [Fact]
    public async Task A_chase_task_names_the_case_it_chases_the_kind_decides_which_and_the_case_belongs_to_the_tasks_patient()
    {
        var (p, v) = await PartiesAsync();
        var r = Referral(p.Id, c => Sent(c)); var l = Lab(v.Id, c => Sent(c)); await AddAsync(r, l);
        await AddAsync(Chase(FollowUpKinds.ReferralFollowUp, r.Id, null, Ann), Chase(FollowUpKinds.LabCaseDue, null, l.Id, Ann));
        Assert.Equal(547, (await AddRefusedAsync(Chase(FollowUpKinds.ReferralFollowUp, null, null, Ann))).Number);                                 // a chase without its case
        Assert.Equal(547, (await AddRefusedAsync(Chase(FollowUpKinds.ReferralFollowUp, null, l.Id, Ann))).Number);                                 // the wrong kind of case
        Assert.Equal(547, (await AddRefusedAsync(Chase(FollowUpKinds.LabCaseDue, r.Id, null, Ann))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Chase(FollowUpKinds.ReferralFollowUp, r.Id, l.Id, Ann))).Number);                                 // both
        Assert.Equal(547, (await AddRefusedAsync(Chase(FollowUpKinds.Manual, r.Id, null, Ann, t => { t.SourceKey = null; t.CreatedByUserId = S.Actor; t.AssigneeUserId = S.Actor; }))).Number);   // a manual task has no case
        Assert.Equal(547, (await AddRefusedAsync(Chase(FollowUpKinds.ReferralFollowUp, r.Id, null, Ann, t => t.SourceKey = null))).Number);        // and a chase has its key
        Assert.Equal(51224, (await AddRefusedAsync(Chase(FollowUpKinds.ReferralFollowUp, r.Id, null, Bo))).Number);                                // another patient's case
        Assert.Equal(2, await CountAsync<FollowUpTask>());
    }

    [Fact]
    public async Task A_chase_task_never_changes_the_case_it_chases_and_the_work_queue_kinds_are_the_five_the_database_allows()
    {
        var (p, v) = await PartiesAsync();
        var r = Referral(p.Id, c => Sent(c)); var r2 = Referral(p.Id, c => Sent(c)); await AddAsync(r, r2);
        var t = Chase(FollowUpKinds.ReferralFollowUp, r.Id, null, Ann); await AddAsync(t);
        Assert.Equal(51210, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [SourceReferralCaseId] = {r2.Id} WHERE [Id] = {t.Id}")).Number);
        Assert.Equal(new[] { "Manual", "Recall", "UnscheduledTreatment", "ReferralFollowUp", "LabCaseDue" }, FollowUpKinds.All);
        Assert.Equal(547, (await AddRefusedAsync(Chase("Mystery", null, null, Ann))).Number);
    }

    // ---------- the migration ----------

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_no_case_data_exists_and_refuses_to_roll_back_over_it()
    {
        await AddAsync(new FollowUpTask { Id = Guid.NewGuid(), PatientId = Ann, AssigneeUserId = Other, Title = "Call", DueAtUtc = DateTimeOffset.UtcNow.AddDays(7), Status = FollowUpStatuses.Open, CreatedByUserId = S.Actor, CreatedAtUtc = DateTimeOffset.UtcNow });
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeExternalCases);
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('ExternalParties','ReferralCases','LabCases','ExternalCaseEvents','ExternalCaseDocuments')").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('FollowUpTasks') AND name IN ('SourceReferralCaseId','SourceLabCaseId')").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM FollowUpTasks").SingleAsync());                           // the old task is untouched
        }
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
            Assert.Equal(5, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('ExternalParties','ReferralCases','LabCases','ExternalCaseEvents','ExternalCaseDocuments')").SingleAsync());

        await PartiesAsync();
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeExternalCases); });
        Assert.Equal(51225, e.Number);
        Assert.Equal(2, await CountAsync<ExternalParty>());
    }
}
