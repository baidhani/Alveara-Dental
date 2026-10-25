using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.ExternalCases;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C02, the party and case services against a real SQL Server: parties are made once, edited with their version and made inactive rather than deleted; a referral or lab case is made once per Idempotency-Key,
/// names an active party of the right kind, moves only along the allowed statuses with the dates, closure and notes each needs, is final when completed or cancelled, and records every change in its history and audit
/// log (ids and status names only, never the reason or a note); two people acting on the same version cannot both win; a document is linked, never copied, and a link that has gone bad is shown as broken.
/// </summary>
public class ExternalCaseServiceTests : TreatmentPlanTestBase
{
    private static DateOnly Today => DateOnly.FromDateTime(PatientTestSupport.Clock.ToPracticeLocal(DateTimeOffset.UtcNow).Date);
    private const string Referral = ExternalCaseKinds.Referral;
    private const string Lab = ExternalCaseKinds.Lab;

    private Task<T> Cases<T>(Func<ExternalCaseService, Task<T>> action) => WithDb(db => action(new ExternalCaseService(db, PatientTestSupport.Clock)));
    private Task<T> Parties<T>(Func<ExternalPartyService, Task<T>> action) => WithDb(db => action(new ExternalPartyService(db, PatientTestSupport.Clock)));

    private static async Task<ExternalCaseException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<ExternalCaseException>(action);

    private async Task<ExternalPartyView> PartyAsync(string kind = ExternalPartyKinds.ReferralProvider, string? name = null) =>
        await Parties(s => s.CreateAsync(S.Actor, new CreatePartyRequest(kind, name ?? $"Party {Guid.NewGuid():N}", "555-010-0100"), default));

    private async Task<ExternalCaseView> NewReferralAsync(Action<CreateReferralRequestBuilder>? change = null, Guid? patient = null, string? key = null)
    {
        var b = new CreateReferralRequestBuilder { PartyId = (await PartyAsync()).Id };
        change?.Invoke(b);
        return (await Cases(s => s.CreateReferralAsync(S.Actor, patient ?? Ann, b.Build(), key ?? Guid.NewGuid().ToString("N"), default))).Case;
    }

    private sealed class CreateReferralRequestBuilder
    {
        public Guid? PartyId; public string? Reason = "Evaluation of an impacted wisdom tooth"; public DateOnly? DueOn; public Guid? EncounterId; public string? Notes;
        public CreateReferralRequest Build() => new(PartyId, Reason, DueOn, EncounterId, Notes);
    }

    private async Task<ExternalCaseView> NewLabAsync(Func<CreateLabRequest, CreateLabRequest>? change = null, Guid? patient = null, string? key = null)
    {
        var request = new CreateLabRequest((await PartyAsync(ExternalPartyKinds.LabVendor)).Id, "Crown, zirconia", null, "36", null, "Zirconia", "A2", null);
        if (change is not null) request = change(request);
        return (await Cases(s => s.CreateLabAsync(S.Actor, patient ?? Ann, request, key ?? Guid.NewGuid().ToString("N"), default))).Case;
    }

    private Task<ExternalCaseResult> MoveAsync(ExternalCaseView c, string to, string? note = null, DateOnly? date = null, string? version = null, string? kind = null) =>
        Cases(s => s.ChangeStatusAsync(S.Actor, kind ?? c.Kind, c.Id, new ChangeStatusRequest(to, note, date, version ?? c.RowVersion), default));

    private async Task<int> CountAsync<T>() where T : class { await using var db = Fixture.CreateContext(); return await db.Set<T>().CountAsync(); }

    private async Task<List<string>> AuditAsync(string typePrefix)
    {
        await using var db = Fixture.CreateContext();
        return await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType.StartsWith(typePrefix)).Select(a => a.Details).ToListAsync();
    }

    // ---------- parties ----------

    [Fact]
    public async Task A_party_is_made_once_per_kind_and_name_a_repeat_answers_with_it_and_other_contact_details_are_a_conflict()
    {
        var p = await PartyAsync(name: "Riverside Oral Surgery");
        var again = await Parties(s => s.CreateAsync(S.Actor, new CreatePartyRequest(p.Kind, "riverside oral surgery", p.ContactDetails), default));
        Assert.Equal(p.Id, again.Id);
        Assert.Equal("party_exists", (await Refused(() => Parties(s => s.CreateAsync(S.Actor, new CreatePartyRequest(p.Kind, p.Name, "other number"), default)))).Code);
        var vendor = await PartyAsync(ExternalPartyKinds.LabVendor, "Riverside Oral Surgery");                                                      // the same name as a lab vendor is another party
        Assert.NotEqual(p.Id, vendor.Id);
        Assert.Equal(2, await CountAsync<ExternalParty>());
        Assert.Single(await AuditAsync("ExternalPartyCreated"), "Outside party created (ReferralProvider).");
    }

    [Theory]
    [InlineData("Dentist", "Name", "kind_invalid")]
    [InlineData(null, "Name", "kind_invalid")]
    [InlineData("LabVendor", "   ", "name_invalid")]
    [InlineData("LabVendor", "two\nlines", "name_invalid")]
    public async Task A_party_needs_a_kind_from_the_closed_set_and_a_plain_one_line_name(string? kind, string name, string code)
    {
        Assert.Equal(code, (await Refused(() => Parties(s => s.CreateAsync(S.Actor, new CreatePartyRequest(kind, name, null), default)))).Code);
        Assert.Equal(0, await CountAsync<ExternalParty>());
    }

    [Fact]
    public async Task A_party_is_edited_with_its_version_and_a_stale_version_a_taken_name_and_no_change_are_refused()
    {
        var a = await PartyAsync(name: "Alpha Lab"); var b = await PartyAsync(name: "Beta Lab");
        var edited = await Parties(s => s.UpdateAsync(S.Actor, a.Id, new UpdatePartyRequest("Alpha Dental Lab", "new number", a.RowVersion), default));
        Assert.Equal(("Alpha Dental Lab", "new number"), (edited.Name, edited.ContactDetails));
        Assert.Equal("party_changed", (await Refused(() => Parties(s => s.UpdateAsync(S.Actor, a.Id, new UpdatePartyRequest("Alpha Again", null, a.RowVersion), default)))).Code);   // the old version
        Assert.Equal("party_exists", (await Refused(() => Parties(s => s.UpdateAsync(S.Actor, b.Id, new UpdatePartyRequest("alpha dental lab", null, b.RowVersion), default)))).Code);
        Assert.Equal("no_change", (await Refused(() => Parties(s => s.UpdateAsync(S.Actor, a.Id, new UpdatePartyRequest("Alpha Dental Lab", "new number", edited.RowVersion), default)))).Code);
        Assert.Equal("row_version_required", (await Refused(() => Parties(s => s.UpdateAsync(S.Actor, a.Id, new UpdatePartyRequest("X", null, null), default)))).Code);
        Assert.Equal("party_not_found", (await Refused(() => Parties(s => s.UpdateAsync(S.Actor, Guid.NewGuid(), new UpdatePartyRequest("X", null, edited.RowVersion), default)))).Code);
    }

    [Fact]
    public async Task A_party_is_made_inactive_instead_of_deleted_drops_out_of_the_default_list_and_can_be_made_active_again()
    {
        var p = await PartyAsync();
        var off = await Parties(s => s.SetActiveAsync(S.Actor, p.Id, new SetPartyActiveRequest(false, p.RowVersion), default));
        Assert.False(off.IsActive);
        Assert.DoesNotContain(await Parties(s => s.ListAsync(null, false, default)), x => x.Id == p.Id);
        Assert.Contains(await Parties(s => s.ListAsync(null, true, default)), x => x.Id == p.Id);
        Assert.False((await Parties(s => s.SetActiveAsync(S.Actor, p.Id, new SetPartyActiveRequest(false, "stale"), default))).IsActive);               // already so: a quiet replay
        var on = await Parties(s => s.SetActiveAsync(S.Actor, p.Id, new SetPartyActiveRequest(true, off.RowVersion), default));
        Assert.True(on.IsActive);
        Assert.Equal("kind_invalid", (await Refused(() => Parties(s => s.ListAsync("Dentist", false, default)))).Code);
    }

    // ---------- creating cases ----------

    [Fact]
    public async Task A_referral_is_a_draft_with_its_history_and_an_audit_entry_that_does_not_carry_the_reason()
    {
        var c = await NewReferralAsync(b => { b.DueOn = Today.AddDays(14); b.EncounterId = AnnEncounter; b.Notes = "Send the panoramic film"; });
        Assert.Equal((Referral, "Draft", Ann), (c.Kind, c.Status, c.PatientId));
        Assert.Equal(new[] { "Sent", "Cancelled" }, c.NextStatuses);
        Assert.False(c.Overdue);
        var events = await Cases(s => s.EventsAsync(Referral, c.Id, default));
        var created = Assert.Single(events);
        Assert.Equal(("Created", "Draft", S.Actor), (created.Kind, created.ToStatus, created.ActorUserId));
        var audit = Assert.Single(await AuditAsync("ReferralCaseCreated"));
        Assert.DoesNotContain("wisdom", audit);
        Assert.DoesNotContain("panoramic", audit);
    }

    [Fact]
    public async Task Making_the_same_referral_twice_with_the_same_key_gives_one_case_and_a_different_request_under_that_key_is_refused()
    {
        var party = await PartyAsync();
        var request = new CreateReferralRequest(party.Id, "Second opinion", null, null, null);
        var first = await Cases(s => s.CreateReferralAsync(S.Actor, Ann, request, "k1", default));
        var again = await Cases(s => s.CreateReferralAsync(S.Actor, Ann, request, "k1", default));
        Assert.Equal((first.Case.Id, false, true), (again.Case.Id, first.Replayed, again.Replayed));
        Assert.Equal("key_reused", (await Refused(() => Cases(s => s.CreateReferralAsync(S.Actor, Ann, request with { Reason = "Something else" }, "k1", default)))).Code);
        Assert.Equal("idempotency_key_required", (await Refused(() => Cases(s => s.CreateReferralAsync(S.Actor, Ann, request, null, default)))).Code);
        var lab = await PartyAsync(ExternalPartyKinds.LabVendor);
        var labRequest = new CreateLabRequest(lab.Id, "Night guard", null, null, null, null, null, null);
        var l1 = await Cases(s => s.CreateLabAsync(S.Actor, Ann, labRequest, "k1", default));
        var l2 = await Cases(s => s.CreateLabAsync(S.Actor, Ann, labRequest, "k1", default));
        Assert.Equal(l1.Case.Id, l2.Case.Id);
        Assert.Equal((1, 1, 2), (await CountAsync<ReferralCase>(), await CountAsync<LabCase>(), await CountAsync<ExternalCaseEvent>()));
    }

    [Fact]
    public async Task Two_identical_creates_at_once_make_one_case()
    {
        var party = await PartyAsync();
        var request = new CreateReferralRequest(party.Id, "Both clicked", null, null, null);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Cases(s => s.CreateReferralAsync(S.Actor, Ann, request, "race-key", default))));
        Assert.Single(results.Select(r => r.Case.Id).Distinct());
        Assert.Equal(1, await CountAsync<ReferralCase>());
        Assert.Equal(1, await CountAsync<ExternalCaseEvent>());
    }

    [Fact]
    public async Task A_case_needs_an_active_outside_party_of_the_matching_kind_and_a_real_patient()
    {
        var lab = await PartyAsync(ExternalPartyKinds.LabVendor); var provider = await PartyAsync();
        var make = (Guid? party, Guid patient) => Cases(s => s.CreateReferralAsync(S.Actor, patient, new CreateReferralRequest(party, "Reason", null, null, null), Guid.NewGuid().ToString("N"), default));
        Assert.Equal("party_required", (await Refused(() => make(null, Ann))).Code);                                                                  // the missing external party
        Assert.Equal("party_not_found", (await Refused(() => make(Guid.NewGuid(), Ann))).Code);
        Assert.Equal("party_kind_invalid", (await Refused(() => make(lab.Id, Ann))).Code);
        await Parties(s => s.SetActiveAsync(S.Actor, provider.Id, new SetPartyActiveRequest(false, provider.RowVersion), default));
        Assert.Equal("party_inactive", (await Refused(() => make(provider.Id, Ann))).Code);
        var other = await PartyAsync();
        Assert.Equal("patient_not_found", (await Refused(() => make(other.Id, Guid.NewGuid()))).Code);
        Assert.Equal("party_kind_invalid", (await Refused(() => Cases(s => s.CreateLabAsync(S.Actor, Ann, new CreateLabRequest(other.Id, "Crown", null, null, null, null, null, null), "kx", default)))).Code);
        Assert.Equal(0, await CountAsync<ReferralCase>() + await CountAsync<LabCase>());
    }

    [Fact]
    public async Task The_text_the_dates_the_tooth_the_encounter_and_the_plan_item_are_checked_before_anything_is_stored()
    {
        var provider = await PartyAsync(); var vendor = await PartyAsync(ExternalPartyKinds.LabVendor);
        async Task<string> Ref(CreateReferralRequest r) => (await Refused(() => Cases(s => s.CreateReferralAsync(S.Actor, Ann, r, Guid.NewGuid().ToString("N"), default)))).Code;
        async Task<string> LabCode(CreateLabRequest r) => (await Refused(() => Cases(s => s.CreateLabAsync(S.Actor, Ann, r, Guid.NewGuid().ToString("N"), default)))).Code;
        var good = new CreateReferralRequest(provider.Id, "Reason", null, null, null);
        Assert.Equal("reason_invalid", await Ref(good with { Reason = "  " }));
        Assert.Equal("reason_invalid", await Ref(good with { Reason = new string('r', 501) }));
        Assert.Equal("note_invalid", await Ref(good with { Notes = new string('n', 1001) }));
        Assert.Equal("due_out_of_range", await Ref(good with { DueOn = Today.AddDays(-366) }));
        Assert.Equal("due_out_of_range", await Ref(good with { DueOn = Today.AddYears(3) }));
        Assert.Equal("encounter_invalid", await Ref(good with { EncounterId = BoEncounter }));
        var goodLab = new CreateLabRequest(vendor.Id, "Crown", null, null, null, null, null, null);
        Assert.Equal("title_invalid", await LabCode(goodLab with { Title = "" }));
        Assert.Equal("tooth_invalid", await LabCode(goodLab with { ToothKey = "99" }));
        Assert.Equal("shade_invalid", await LabCode(goodLab with { Shade = new string('s', 51) }));
        Assert.Equal("material_invalid", await LabCode(goodLab with { Material = "x\ty\u0001" }));
        var boItem = await PlanItemAsync(Bo);
        Assert.Equal("plan_item_invalid", await LabCode(goodLab with { TreatmentPlanItemId = boItem }));
        Assert.Equal(0, await CountAsync<ReferralCase>() + await CountAsync<LabCase>());
    }

    private async Task<Guid> PlanItemAsync(Guid patient, string? tooth = null)
    {
        var plan = new TreatmentPlan { Id = Guid.NewGuid(), PatientId = patient, IdempotencyKey = $"plan-{Guid.NewGuid():N}", Title = "Plan", Status = TreatmentPlanStatuses.Proposed, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };
        var procedure = tooth is null ? await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]) : await ProcedureAsync($"LOCAL-{Guid.NewGuid():N}"[..14], ProcedureScopes.Tooth, ProcedureDentitions.Both);
        var item = new TreatmentPlanItem
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = patient, ItemNumber = 1, IdempotencyKey = "item-1", DiagnosisId = await DiagnosisAsync(patient), ProcedureId = procedure.Summary.Id,
            ProcedureVersionId = procedure.Versions[0].VersionId, Fee = procedure.Versions[0].Fee, ToothKey = tooth, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor,
        };
        await using var db = Fixture.CreateContext();
        db.AddRange(plan, item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    [Fact]
    public async Task A_lab_case_for_a_planned_treatment_takes_the_planned_tooth_unless_another_is_given()
    {
        var item = await PlanItemAsync(Ann, "14");
        var vendor = await PartyAsync(ExternalPartyKinds.LabVendor);
        var defaulted = (await Cases(s => s.CreateLabAsync(S.Actor, Ann, new CreateLabRequest(vendor.Id, "Crown", null, null, item, "PFM", "A3", null), "d1", default))).Case;
        Assert.Equal(("14", item), (defaulted.ToothKey, defaulted.TreatmentPlanItemId));
        var explicitTooth = (await Cases(s => s.CreateLabAsync(S.Actor, Ann, new CreateLabRequest(vendor.Id, "Crown", null, "15", item, null, null, null), "d2", default))).Case;
        Assert.Equal("15", explicitTooth.ToothKey);
        Assert.Equal(("Lab", "Crown", "PFM", "A3"), (defaulted.Kind, defaulted.Summary, defaulted.Material, defaulted.Shade));
    }

    // ---------- lifecycles ----------

    [Fact]
    public async Task A_referral_goes_from_draft_to_sent_to_received_to_completed_with_its_dates_closure_and_history()
    {
        var c = await NewReferralAsync(b => b.DueOn = Today.AddDays(10));
        var sent = (await MoveAsync(c, "Sent", date: Today.AddDays(-2))).Case;
        Assert.Equal(("Sent", Today.AddDays(-2), null), (sent.Status, sent.SentOn, sent.ReceivedOn));
        var received = (await MoveAsync(sent, "Received", date: Today.AddDays(-1))).Case;
        Assert.Equal(("Received", Today.AddDays(-1)), (received.Status, received.ReceivedOn));
        var done = (await MoveAsync(received, "Completed", note: "Report reviewed with the patient")).Case;
        Assert.Equal(("Completed", "Report reviewed with the patient"), (done.Status, done.ClosureNote));
        Assert.NotNull(done.ClosedAtUtc);
        Assert.Empty(done.NextStatuses);
        var events = await Cases(s => s.EventsAsync(Referral, c.Id, default));
        Assert.Equal(new[] { "Created", "StatusChanged", "StatusChanged", "StatusChanged" }, events.Select(e => e.Kind));
        Assert.Equal(new[] { ("Draft", "Sent"), ("Sent", "Received"), ("Received", "Completed") }, events.Where(e => e.Kind == "StatusChanged").Select(e => (e.FromStatus!, e.ToStatus!)));
        Assert.Equal(3, (await AuditAsync("ReferralCaseStatusChanged")).Count);
        Assert.DoesNotContain(await AuditAsync("ReferralCaseStatusChanged"), a => a.Contains("Report reviewed"));
    }

    [Fact]
    public async Task A_lab_case_goes_out_is_acknowledged_comes_back_and_may_be_sent_back_for_a_remake_which_needs_a_note()
    {
        var c = await NewLabAsync();
        var sent = (await MoveAsync(c, "Sent")).Case;
        Assert.Equal(Today, sent.SentOn);                                                                                                            // today when no date is given
        var working = (await MoveAsync(sent, "InProgress")).Case;
        var back = (await MoveAsync(working, "Received", date: Today)).Case;
        Assert.Equal("note_required", (await Refused(() => MoveAsync(back, "Sent"))).Code);
        var remake = (await MoveAsync(back, "Sent", note: "Margin open on the mesial")).Case;
        Assert.Equal(("Sent", null), (remake.Status, remake.ReceivedOn));                                                                            // the clock starts again
        var again = (await MoveAsync((await MoveAsync(remake, "Received")).Case, "Completed")).Case;
        Assert.Equal("Completed", again.Status);
        Assert.Equal(7, (await Cases(s => s.EventsAsync(Lab, c.Id, default))).Count);
    }

    [Fact]
    public async Task A_cancellation_needs_its_reason_and_a_cancelled_case_is_final()
    {
        var c = await NewReferralAsync();
        Assert.Equal("note_required", (await Refused(() => MoveAsync(c, "Cancelled"))).Code);
        Assert.Equal("note_required", (await Refused(() => MoveAsync(c, "Cancelled", note: "   "))).Code);
        var cancelled = (await MoveAsync(c, "Cancelled", note: "Patient moved away")).Case;
        Assert.Equal(("Cancelled", "Patient moved away"), (cancelled.Status, cancelled.ClosureNote));
        Assert.Equal("status_invalid", (await Refused(() => MoveAsync(cancelled, "Sent"))).Code);
        Assert.Equal("case_closed", (await Refused(() => Cases(s => s.UpdateReferralAsync(S.Actor, c.Id, new UpdateReferralRequest(c.PartyId, "Edited", null, null, null, cancelled.RowVersion), default)))).Code);
    }

    [Fact]
    public async Task A_move_the_rules_do_not_allow_an_unknown_status_and_a_bad_date_are_refused_and_store_nothing()
    {
        var c = await NewReferralAsync();
        Assert.Equal("status_invalid", (await Refused(() => MoveAsync(c, "Completed"))).Code);                                                       // skips a step
        Assert.Equal("status_invalid", (await Refused(() => MoveAsync(c, "InProgress"))).Code);                                                     // a lab status on a referral
        Assert.Equal("status_invalid", (await Refused(() => MoveAsync(c, "Done"))).Code);
        Assert.Equal("date_invalid", (await Refused(() => MoveAsync(c, "Sent", date: Today.AddDays(1)))).Code);
        var sent = (await MoveAsync(c, "Sent", date: Today.AddDays(-1))).Case;
        Assert.Equal("date_before_sent", (await Refused(() => MoveAsync(sent, "Received", date: Today.AddDays(-2)))).Code);
        Assert.Equal("kind_invalid", (await Refused(() => Cases(s => s.ChangeStatusAsync(S.Actor, "Dentist", c.Id, new ChangeStatusRequest("Sent", null, null, c.RowVersion), default)))).Code);
        Assert.Equal("case_not_found", (await Refused(() => Cases(s => s.ChangeStatusAsync(S.Actor, Referral, Guid.NewGuid(), new ChangeStatusRequest("Sent", null, null, c.RowVersion), default)))).Code);
        Assert.Equal(2, await CountAsync<ExternalCaseEvent>());                                                                                      // created, sent: nothing else was recorded
    }

    // ---------- concurrency ----------

    [Fact]
    public async Task A_stale_version_is_refused_and_two_people_moving_the_same_case_at_once_cannot_both_win()
    {
        var c = await NewReferralAsync();
        var first = await MoveAsync(c, "Sent");
        Assert.Equal("case_changed", (await Refused(() => MoveAsync(c, "Cancelled", note: "late", version: c.RowVersion))).Code);                  // the version the screen was showing
        Assert.Equal("Sent", (await Cases(s => s.GetAsync(Referral, c.Id, default))).Status);
        Assert.Equal("row_version_required", (await Refused(() => MoveAsync(first.Case, "Received", version: "!!"))).Code);

        var d = await NewReferralAsync();
        var outcomes = await Task.WhenAll(new[] { "Sent", "Cancelled" }.Select(to => Task.Run(async () =>
        {
            try { await MoveAsync(d, to, note: "x"); return "ok"; }
            catch (ExternalCaseException e) { return e.Code; }
        })));
        Assert.Equal(1, outcomes.Count(o => o == "ok"));
        Assert.Equal(1, outcomes.Count(o => o is "case_changed" or "status_invalid"));                                                              // the loser is told either that the case changed or (if it read the case after a cancellation) that the move is no longer allowed
        Assert.Equal(2, (await Cases(s => s.EventsAsync(Referral, d.Id, default))).Count);                                                           // created + exactly one move
    }

    [Fact]
    public async Task An_edit_needs_the_current_version_changes_something_and_a_sent_case_keeps_its_party()
    {
        var c = await NewReferralAsync(b => b.DueOn = Today.AddDays(5));
        var other = await PartyAsync();
        var edited = (await Cases(s => s.UpdateReferralAsync(S.Actor, c.Id, new UpdateReferralRequest(other.Id, "Updated reason", Today.AddDays(9), null, "Bring the film", c.RowVersion), default))).Case;   // a draft may change party
        Assert.Equal((other.Id, "Updated reason", Today.AddDays(9)), (edited.PartyId, edited.Summary, edited.DueOn));
        Assert.Equal("case_changed", (await Refused(() => Cases(s => s.UpdateReferralAsync(S.Actor, c.Id, new UpdateReferralRequest(other.Id, "Again", null, null, null, c.RowVersion), default)))).Code);
        Assert.Equal("no_change", (await Refused(() => Cases(s => s.UpdateReferralAsync(S.Actor, c.Id, new UpdateReferralRequest(other.Id, "Updated reason", Today.AddDays(9), null, "Bring the film", edited.RowVersion), default)))).Code);
        var sent = (await MoveAsync(edited, "Sent")).Case;
        var third = await PartyAsync();
        Assert.Equal("party_locked", (await Refused(() => Cases(s => s.UpdateReferralAsync(S.Actor, c.Id, new UpdateReferralRequest(third.Id, "Updated reason", Today.AddDays(9), null, "Bring the film", sent.RowVersion), default)))).Code);
        var chased = (await Cases(s => s.UpdateReferralAsync(S.Actor, c.Id, new UpdateReferralRequest(other.Id, "Updated reason", Today.AddDays(-1), null, "Bring the film", sent.RowVersion), default))).Case;   // the due date can change after sending
        Assert.True(chased.Overdue);                                                                                                                 // and a sent case past its due date is overdue
    }

    [Fact]
    public async Task A_lab_case_is_edited_the_same_way()
    {
        var c = await NewLabAsync();
        var edited = (await Cases(s => s.UpdateLabAsync(S.Actor, c.Id, new UpdateLabRequest(c.PartyId, "Bridge, 3 units", Today.AddDays(7), "36", null, "PFM", "B1", "Rush", c.RowVersion), default))).Case;
        Assert.Equal(("Bridge, 3 units", "PFM", "B1", Today.AddDays(7)), (edited.Summary, edited.Material, edited.Shade, edited.DueOn));
        Assert.Equal("case_changed", (await Refused(() => Cases(s => s.UpdateLabAsync(S.Actor, c.Id, new UpdateLabRequest(c.PartyId, "Stale", null, null, null, null, null, null, c.RowVersion), default)))).Code);
        Assert.Equal(2, (await Cases(s => s.EventsAsync(Lab, c.Id, default))).Count);
    }

    // ---------- documents ----------

    private async Task<PatientDocument> DocumentAsync(Guid patient, Action<PatientDocument>? change = null)
    {
        var d = new PatientDocument
        {
            Id = Guid.NewGuid(), PatientId = patient, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), Description = "Panoramic film.", OriginalFileName = "pano.png", DetectedType = "png",
            ContentType = "image/png", SizeBytes = 2048, Sha256 = new string('a', 64), BlobId = Guid.NewGuid(), ImportedByUserId = S.Actor, ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = new string('a', 64),
        };
        change?.Invoke(d);
        await using var db = Fixture.CreateContext();
        db.Add(d);
        await db.SaveChangesAsync();
        return d;
    }

    [Fact]
    public async Task A_document_is_linked_not_copied_linking_it_twice_is_a_quiet_replay_and_unlinking_leaves_the_document()
    {
        var c = await NewReferralAsync(); var doc = await DocumentAsync(Ann);
        var linked = await Cases(s => s.LinkDocumentAsync(S.Actor, Referral, c.Id, doc.Id, default));
        Assert.Equal((true, false), (linked.Linked, linked.Replayed));
        Assert.True((await Cases(s => s.LinkDocumentAsync(S.Actor, Referral, c.Id, doc.Id, default))).Replayed);
        var docs = await Cases(s => s.DocumentsAsync(Referral, c.Id, default));
        var shown = Assert.Single(docs);
        Assert.Equal((doc.Id, "pano.png", "Radiograph", false), (shown.DocumentId, shown.FileName, shown.Category, shown.Broken));
        Assert.Equal(1, await CountAsync<PatientDocument>());                                                                                        // nothing was copied
        var unlinked = await Cases(s => s.UnlinkDocumentAsync(S.Actor, Referral, c.Id, doc.Id, default));
        Assert.Equal((false, false), (unlinked.Linked, unlinked.Replayed));
        Assert.True((await Cases(s => s.UnlinkDocumentAsync(S.Actor, Referral, c.Id, doc.Id, default))).Replayed);
        Assert.Empty(await Cases(s => s.DocumentsAsync(Referral, c.Id, default)));
        Assert.Equal(1, await CountAsync<PatientDocument>());                                                                                        // the document is still in the library
        var kinds = (await Cases(s => s.EventsAsync(Referral, c.Id, default))).Select(e => e.Kind);
        Assert.Equal(new[] { "Created", "DocumentLinked", "DocumentUnlinked" }, kinds);                                                              // one entry each despite the repeats
        Assert.Single(await AuditAsync("CaseDocumentLinked")); Assert.Single(await AuditAsync("CaseDocumentUnlinked"));
    }

    [Fact]
    public async Task A_document_of_another_patient_or_an_inactive_one_cannot_be_linked_and_a_finished_case_takes_no_links()
    {
        var c = await NewLabAsync(); var boDoc = await DocumentAsync(Bo); var annDoc = await DocumentAsync(Ann);
        Assert.Equal("document_invalid", (await Refused(() => Cases(s => s.LinkDocumentAsync(S.Actor, Lab, c.Id, boDoc.Id, default)))).Code);
        Assert.Equal("document_invalid", (await Refused(() => Cases(s => s.LinkDocumentAsync(S.Actor, Lab, c.Id, Guid.NewGuid(), default)))).Code);
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [Status] = N'Inactive', [InactiveReason] = N'Wrong patient', [InactivatedByUserId] = {S.Actor}, [InactivatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = {annDoc.Id}");
        Assert.Equal("document_inactive", (await Refused(() => Cases(s => s.LinkDocumentAsync(S.Actor, Lab, c.Id, annDoc.Id, default)))).Code);
        var fresh = await DocumentAsync(Ann);
        var cancelled = (await MoveAsync(c, "Cancelled", note: "Not needed")).Case;
        Assert.Equal("case_closed", (await Refused(() => Cases(s => s.LinkDocumentAsync(S.Actor, Lab, c.Id, fresh.Id, default)))).Code);
        Assert.Equal("case_closed", (await Refused(() => Cases(s => s.UnlinkDocumentAsync(S.Actor, Lab, c.Id, fresh.Id, default)))).Code);
        Assert.Equal(0, await CountAsync<ExternalCaseDocument>());
        Assert.Equal("Cancelled", cancelled.Status);
    }

    [Fact]
    public async Task A_link_whose_document_went_bad_after_linking_is_shown_as_broken_with_the_reason_and_not_dropped()
    {
        var c = await NewReferralAsync(); var inactive = await DocumentAsync(Ann); var missing = await DocumentAsync(Ann); var corrupt = await DocumentAsync(Ann); var fine = await DocumentAsync(Ann);
        foreach (var d in new[] { inactive, missing, corrupt, fine }) await Cases(s => s.LinkDocumentAsync(S.Actor, Referral, c.Id, d.Id, default));
        await using (var db = Fixture.CreateContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [Status] = N'Inactive', [InactiveReason] = N'Scanned in error', [InactivatedByUserId] = {S.Actor}, [InactivatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = {inactive.Id}");
            db.DocumentIntegrity.AddRange(new DocumentIntegrity { DocumentId = missing.Id, Status = IntegrityStatuses.Missing, CheckedAtUtc = DateTimeOffset.UtcNow }, new DocumentIntegrity { DocumentId = corrupt.Id, Status = IntegrityStatuses.Corrupt, CheckedAtUtc = DateTimeOffset.UtcNow },
                new DocumentIntegrity { DocumentId = fine.Id, Status = IntegrityStatuses.Ok, CheckedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var docs = (await Cases(s => s.DocumentsAsync(Referral, c.Id, default))).ToDictionary(d => d.DocumentId);
        Assert.Equal(4, docs.Count);
        Assert.Equal((true, "The document was made inactive."), (docs[inactive.Id].Broken, docs[inactive.Id].BrokenReason));
        Assert.Equal((true, "The document's file is missing from storage."), (docs[missing.Id].Broken, docs[missing.Id].BrokenReason));
        Assert.Equal((true, "The document's file failed its integrity check."), (docs[corrupt.Id].Broken, docs[corrupt.Id].BrokenReason));
        Assert.False(docs[fine.Id].Broken);
    }

    // ---------- audit ----------

    [Fact]
    public async Task Every_change_to_a_case_is_in_the_audit_log_with_ids_and_status_names_only()
    {
        var c = await NewLabAsync(r => r with { Title = "Secret title about the patient", Notes = "Private note" });
        var sent = (await MoveAsync(c, "Sent")).Case;
        await Cases(s => s.UpdateLabAsync(S.Actor, c.Id, new UpdateLabRequest(c.PartyId, "Secret title about the patient", Today.AddDays(3), "36", null, "Zirconia", "A2", "Private note", sent.RowVersion), default));
        await using var db = Fixture.CreateContext();
        var entries = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType.StartsWith("LabCase")).Select(a => new { a.EventType, Description = a.Details }).ToListAsync();
        Assert.Equal(new[] { "LabCaseChanged", "LabCaseCreated", "LabCaseStatusChanged" }, entries.Select(e => e.EventType).OrderBy(x => x));
        Assert.DoesNotContain(entries, e => (e.Description ?? "").Contains("Secret") || (e.Description ?? "").Contains("Private"));
    }
}
