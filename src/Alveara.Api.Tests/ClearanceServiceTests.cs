using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Safety;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N011 clearance tracking, against real SQL Server: requested -> received -> resolved (or cancelled) with who, when and why at every step; received is not resolved; a clearance
/// still waiting cannot be resolved; resolving and cancelling need a reason; the supporting document may not be available yet and can be attached later; history, audit, repeats,
/// stale edits and simultaneous requests.
/// </summary>
public class ClearanceServiceTests : SafetyTestBase
{
    private Task<SafetyContext> RequestAsync(string kind = "Medical", string reason = "Cardiac clearance before extraction", string? from = "Dr. Singh, cardiology", Guid? patient = null, Guid? actor = null) =>
        ClearancesAsync(s => s.RequestAsync(patient ?? Ann, kind, reason, from, actor ?? S.Actor, default));

    private async Task<ClearanceView> RequestedAsync(string kind = "Medical", string reason = "Cardiac clearance before extraction") => ClearanceOf(await RequestAsync(kind, reason), kind);

    // ---------- the workflow ----------

    [Fact]
    public async Task A_clearance_is_requested_received_and_resolved_with_who_when_and_why_at_every_step()
    {
        var c = await RequestAsync();
        var asked = ClearanceOf(c);
        Assert.Equal((ClearanceStatuses.Requested, "Dr. Okafor", "Dr. Singh, cardiology", false), (asked.Status, asked.RequestedByName, asked.RequestedFrom, asked.DocumentPending));
        Assert.Equal(1, c.Summary.OpenClearanceCount);

        c = await ClearancesAsync(s => s.ReceiveAsync(asked.Id, "Letter faxed back", "cardiac-clearance-2030-01-10.pdf", asked.RowVersion, Other, default));
        var got = ClearanceOf(c);
        Assert.Equal((ClearanceStatuses.Received, "Hana Hygienist", "Letter faxed back", "cardiac-clearance-2030-01-10.pdf", false), (got.Status, got.ReceivedByName, got.ReceivedNote, got.DocumentReference, got.DocumentPending));
        Assert.NotNull(got.ReceivedAtUtc);
        Assert.Equal(1, c.Summary.OpenClearanceCount);                    // received is NOT resolved: still open until someone resolves it

        c = await ClearancesAsync(s => s.ResolveAsync(got.Id, "Cleared for extraction with epinephrine limits", got.RowVersion, S.Actor, default));
        var done = ClearanceOf(c);
        Assert.Equal((ClearanceStatuses.Resolved, "Dr. Okafor", "Cleared for extraction with epinephrine limits"), (done.Status, done.ClosedByName, done.ClosingReason));
        Assert.NotNull(done.ClosedAtUtc);
        Assert.Equal(0, c.Summary.OpenClearanceCount);

        var history = await WithDb(db => new ClearanceService(db, Clock).HistoryAsync(done.Id, default));
        Assert.Equal(new[] { ClearanceChangeTypes.Requested, ClearanceChangeTypes.Received, ClearanceChangeTypes.Resolved }, history.Versions.Select(v => v.ChangeType));
        Assert.Equal(new[] { ClearanceStatuses.Requested, ClearanceStatuses.Received, ClearanceStatuses.Resolved }, history.Versions.Select(v => v.Status));
        Assert.Equal(new[] { "Dr. Okafor", "Hana Hygienist", "Dr. Okafor" }, history.Versions.Select(v => v.ActorName));
        Assert.Equal(new string?[] { null, "Letter faxed back", "Cleared for extraction with epinephrine limits" }, history.Versions.Select(v => v.Note));
    }

    [Fact]
    public async Task A_clearance_still_waiting_cannot_be_resolved_and_nothing_changes()
    {
        var asked = await RequestedAsync();
        var refused = await RefusedAsync(() => ClearancesAsync(s => s.ResolveAsync(asked.Id, "Assumed fine", asked.RowVersion, S.Actor, default)));
        Assert.Equal(("clearance_not_received", 409), (refused.Code, refused.StatusCode));
        Assert.Equal(ClearanceStatuses.Requested, ClearanceOf(await ContextAsync()).Status);
        Assert.Single(await WithDb(db => db.ClearanceVersions.ToListAsync()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Resolving_or_cancelling_needs_a_reason(string? reason)
    {
        var asked = await RequestedAsync();
        var got = ClearanceOf(await ClearancesAsync(s => s.ReceiveAsync(asked.Id, null, null, asked.RowVersion, S.Actor, default)));
        var resolve = await RefusedAsync(() => ClearancesAsync(s => s.ResolveAsync(got.Id, reason, got.RowVersion, S.Actor, default)));
        var cancel = await RefusedAsync(() => ClearancesAsync(s => s.CancelAsync(got.Id, reason, got.RowVersion, S.Actor, default)));
        Assert.Equal(("reason_required", "reason_required"), (resolve.Code, cancel.Code));
        Assert.True(resolve.FieldErrors.ContainsKey("reason") && cancel.FieldErrors.ContainsKey("reason"));
        Assert.Equal(ClearanceStatuses.Received, ClearanceOf(await ContextAsync()).Status);
    }

    [Fact]
    public async Task A_clearance_that_is_no_longer_needed_is_cancelled_with_a_reason_and_stays_listed_closed()
    {
        var asked = await RequestedAsync("Dental", "Specialist opinion on the implant site");
        var c = await ClearancesAsync(s => s.CancelAsync(asked.Id, "Patient chose a different treatment", asked.RowVersion, Other, default));
        var closed = ClearanceOf(c, "Dental");
        Assert.Equal((ClearanceStatuses.Cancelled, "Hana Hygienist", "Patient chose a different treatment"), (closed.Status, closed.ClosedByName, closed.ClosingReason));
        Assert.Equal(0, c.Summary.OpenClearanceCount);
        Assert.Single(c.Clearances);                                      // kept, not deleted

        var again = await ClearancesAsync(s => s.CancelAsync(asked.Id, "Again", closed.RowVersion, S.Actor, default));
        Assert.Equal(closed.RowVersion, ClearanceOf(again, "Dental").RowVersion);
        Assert.Equal("clearance_closed", (await RefusedAsync(() => ClearancesAsync(s => s.ReceiveAsync(asked.Id, null, null, closed.RowVersion, S.Actor, default)))).Code);
        Assert.Equal("clearance_closed", (await RefusedAsync(() => ClearancesAsync(s => s.ResolveAsync(asked.Id, "x", closed.RowVersion, S.Actor, default)))).Code);
        Assert.Equal("clearance_closed", (await RefusedAsync(() => ClearancesAsync(s => s.AttachDocumentAsync(asked.Id, "doc.pdf", closed.RowVersion, S.Actor, default)))).Code);
    }

    [Fact]
    public async Task A_resolved_clearance_cannot_be_cancelled_or_changed()
    {
        var asked = await RequestedAsync();
        var got = ClearanceOf(await ClearancesAsync(s => s.ReceiveAsync(asked.Id, null, "letter.pdf", asked.RowVersion, S.Actor, default)));
        var done = ClearanceOf(await ClearancesAsync(s => s.ResolveAsync(got.Id, "Cleared", got.RowVersion, S.Actor, default)));
        Assert.Equal("clearance_closed", (await RefusedAsync(() => ClearancesAsync(s => s.CancelAsync(done.Id, "Oops", done.RowVersion, S.Actor, default)))).Code);
        var again = ClearanceOf(await ClearancesAsync(s => s.ResolveAsync(done.Id, "Cleared again", done.RowVersion, Other, default)));
        Assert.Equal((done.RowVersion, done.ClosedByName, done.ClosingReason), (again.RowVersion, again.ClosedByName, again.ClosingReason)); // the first resolution stands
    }

    // ---------- the supporting document ----------

    [Fact]
    public async Task A_clearance_can_be_received_and_resolved_without_its_document_and_says_so_until_it_is_attached()
    {
        var asked = await RequestedAsync();
        var got = ClearanceOf(await ClearancesAsync(s => s.ReceiveAsync(asked.Id, "Verbal clearance by phone", null, asked.RowVersion, S.Actor, default)));
        Assert.Equal((ClearanceStatuses.Received, (string?)null, true), (got.Status, got.DocumentReference, got.DocumentPending));      // the document is not yet available

        var done = ClearanceOf(await ClearancesAsync(s => s.ResolveAsync(got.Id, "Cleared on the physician's word; letter to follow", got.RowVersion, S.Actor, default)));
        Assert.True(done.DocumentPending);                                // resolved, but still flagged: no document attached
        Assert.Contains(await AuditAsync(nameof(Clearance)), e => e.Type == "ClearanceResolved" && e.Details.Contains("without a supporting document"));

        var attached = ClearanceOf(await ClearancesAsync(s => s.AttachDocumentAsync(done.Id, "cardiac-letter-2030-01-12.pdf", done.RowVersion, Other, default)));
        Assert.Equal(("cardiac-letter-2030-01-12.pdf", false, ClearanceStatuses.Resolved), (attached.DocumentReference, attached.DocumentPending, attached.Status));
        var history = await WithDb(db => new ClearanceService(db, Clock).HistoryAsync(done.Id, default));
        Assert.Equal(new[] { ClearanceChangeTypes.Requested, ClearanceChangeTypes.Received, ClearanceChangeTypes.Resolved, ClearanceChangeTypes.DocumentAttached }, history.Versions.Select(v => v.ChangeType));
        Assert.Null(history.Versions[2].DocumentReference);               // what it said before the document arrived is still there
    }

    [Fact]
    public async Task A_document_cannot_be_attached_before_the_clearance_is_received_must_say_which_document_and_attaching_it_again_is_quiet()
    {
        var asked = await RequestedAsync();
        Assert.Equal("clearance_not_received", (await RefusedAsync(() => ClearancesAsync(s => s.AttachDocumentAsync(asked.Id, "x.pdf", asked.RowVersion, S.Actor, default)))).Code);
        var got = ClearanceOf(await ClearancesAsync(s => s.ReceiveAsync(asked.Id, null, null, asked.RowVersion, S.Actor, default)));
        var blank = await RefusedAsync(() => ClearancesAsync(s => s.AttachDocumentAsync(got.Id, "  ", got.RowVersion, S.Actor, default)));
        Assert.True(blank.FieldErrors.ContainsKey("documentReference"));
        var with = ClearanceOf(await ClearancesAsync(s => s.AttachDocumentAsync(got.Id, "x.pdf", got.RowVersion, S.Actor, default)));
        var same = ClearanceOf(await ClearancesAsync(s => s.AttachDocumentAsync(got.Id, "x.pdf", with.RowVersion, Other, default)));
        Assert.Equal(with.RowVersion, same.RowVersion);
        Assert.Equal(3, (await WithDb(db => new ClearanceService(db, Clock).HistoryAsync(got.Id, default))).Versions.Count);
    }

    // ---------- validation, repeats and uniqueness ----------

    [Theory]
    [InlineData("Surgical", "Needs it", "kind")]
    [InlineData("medical", "Needs it", "kind")]
    [InlineData("", "Needs it", "kind")]
    [InlineData("Medical", "", "reason")]
    [InlineData("Medical", "   ", "reason")]
    public async Task A_clearance_needs_a_valid_kind_and_a_reason(string kind, string reason, string field)
    {
        var refused = await RefusedAsync(() => RequestAsync(kind, reason));
        Assert.Equal(("validation_failed", 400), (refused.Code, refused.StatusCode));
        Assert.True(refused.FieldErrors.ContainsKey(field));
        Assert.Equal(0, await S.CountAsync(db => db.Clearances));
    }

    [Fact]
    public async Task Limits_are_enforced_an_unknown_patient_is_refused_and_one_patients_clearance_never_shows_on_another()
    {
        var refused = await RefusedAsync(() => RequestAsync(reason: new string('r', SafetyRules.ReasonMax + 1), from: new string('f', SafetyRules.NameMax + 1)));
        Assert.Equal(new[] { "reason", "requestedFrom" }, refused.FieldErrors.Keys.Order());
        Assert.Equal("patient_not_found", (await RefusedAsync(() => RequestAsync(patient: Guid.NewGuid()))).Code);
        await RequestAsync();
        Assert.Empty((await ContextAsync(Bo)).Clearances);
    }

    [Fact]
    public async Task Requesting_the_same_open_clearance_again_changes_nothing_and_a_different_reason_or_kind_is_a_separate_clearance()
    {
        await RequestAsync();
        await RequestAsync(reason: "CARDIAC CLEARANCE BEFORE EXTRACTION");          // a retried request, retyped
        Assert.Equal(1, await S.CountAsync(db => db.Clearances));
        await RequestAsync(reason: "Antibiotic prophylaxis review");
        await RequestAsync("Dental", "Cardiac clearance before extraction");
        Assert.Equal(3, await S.CountAsync(db => db.Clearances));
        Assert.Equal(3, (await ContextAsync()).Summary.OpenClearanceCount);

        var asked = ClearanceOf(await ContextAsync(), "Dental");
        await ClearancesAsync(s => s.CancelAsync(asked.Id, "Not needed", asked.RowVersion, S.Actor, default));
        await RequestAsync("Dental", "Cardiac clearance before extraction");              // the closed one does not block asking again
        Assert.Equal(4, await S.CountAsync(db => db.Clearances));
    }

    [Fact]
    public async Task Receiving_a_received_clearance_changes_nothing()
    {
        var asked = await RequestedAsync();
        var got = ClearanceOf(await ClearancesAsync(s => s.ReceiveAsync(asked.Id, "First", "a.pdf", asked.RowVersion, S.Actor, default)));
        var again = ClearanceOf(await ClearancesAsync(s => s.ReceiveAsync(asked.Id, "Second", "b.pdf", got.RowVersion, Other, default)));
        Assert.Equal((got.RowVersion, "First", "a.pdf", "Dr. Okafor"), (again.RowVersion, again.ReceivedNote, again.DocumentReference, again.ReceivedByName));
        Assert.Equal(2, (await WithDb(db => new ClearanceService(db, Clock).HistoryAsync(asked.Id, default))).Versions.Count);
    }

    [Fact]
    public async Task Six_simultaneous_requests_for_the_same_clearance_open_exactly_one()
    {
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await RequestAsync(); return "ok"; }
            catch (Exception ex) { return ex.GetType().Name; }
        })));
        Assert.All(outcomes, o => Assert.Equal("ok", o));
        Assert.Equal(1, await S.CountAsync(db => db.Clearances));
        Assert.Equal(1, await S.CountAsync(db => db.ClearanceVersions));
    }

    // ---------- stale edits, audit, failure ----------

    [Fact]
    public async Task A_stale_edit_is_refused_and_a_missing_or_unknown_version_is_rejected_before_anything_changes()
    {
        var asked = await RequestedAsync();
        await ClearancesAsync(s => s.ReceiveAsync(asked.Id, null, null, asked.RowVersion, S.Actor, default));                              // first clinician, from V
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => ClearancesAsync(s => s.CancelAsync(asked.Id, "Second", asked.RowVersion, Other, default)));
        Assert.Equal(ClearanceStatuses.Received, ClearanceOf(await ContextAsync()).Status);
        Assert.Equal("row_version_required", (await RefusedAsync(() => ClearancesAsync(s => s.ReceiveAsync(asked.Id, null, null, null, S.Actor, default)))).Code);
        Assert.Equal("row_version_invalid", (await RefusedAsync(() => ClearancesAsync(s => s.ReceiveAsync(asked.Id, null, null, "not base64!", S.Actor, default)))).Code);
        Assert.Equal("clearance_not_found", (await RefusedAsync(() => ClearancesAsync(s => s.ReceiveAsync(Guid.NewGuid(), null, null, asked.RowVersion, S.Actor, default)))).Code);
        Assert.Equal("clearance_not_found", (await RefusedAsync(() => WithDb(db => new ClearanceService(db, Clock).HistoryAsync(Guid.NewGuid(), default)))).Code);
        Assert.Equal(2, (await WithDb(db => new ClearanceService(db, Clock).HistoryAsync(asked.Id, default))).Versions.Count);
    }

    [Fact]
    public async Task Every_step_is_audited_with_user_and_time_and_no_audit_text_contains_the_clearance_content()
    {
        var asked = await RequestedAsync("Medical", "HIV viral load review before surgery");
        var got = ClearanceOf(await ClearancesAsync(s => s.ReceiveAsync(asked.Id, "Lab result attached", "lab-2030.pdf", asked.RowVersion, Other, default)));
        await ClearancesAsync(s => s.ResolveAsync(got.Id, "Viral load undetectable", got.RowVersion, S.Actor, default));
        var audit = await AuditAsync(nameof(Clearance));
        Assert.Equal(new[] { "ClearanceRequested", "ClearanceReceived", "ClearanceResolved" }, audit.Select(e => e.Type));
        Assert.Equal(new Guid?[] { S.Actor, Other, S.Actor }, audit.Select(e => e.By));
        var text = string.Join(" ", audit.Select(e => e.Details));
        foreach (var secret in new[] { "HIV", "viral", "Lab result", "lab-2030", "undetectable", "Singh" }) Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task If_the_audit_entry_cannot_be_written_nothing_is_saved_and_the_same_request_works_on_retry()
    {
        await using (var failing = S.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new ClearanceService(failing, Clock).RequestAsync(Ann, "Medical", "Cardiac clearance", null, S.Actor, default));
        Assert.Equal((0, 0), (await S.CountAsync(db => db.Clearances), await S.CountAsync(db => db.ClearanceVersions)));

        var asked = await RequestedAsync();
        await using (var failing = S.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new ClearanceService(failing, Clock).ReceiveAsync(asked.Id, null, null, asked.RowVersion, S.Actor, default));
        Assert.Equal(ClearanceStatuses.Requested, ClearanceOf(await ContextAsync()).Status);
        Assert.Equal(ClearanceStatuses.Received, ClearanceOf(await ClearancesAsync(s => s.ReceiveAsync(asked.Id, null, null, asked.RowVersion, S.Actor, default))).Status);
    }
}
