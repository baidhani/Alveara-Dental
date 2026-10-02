using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Patients;
using Xunit;
using static Alveara.Api.Tests.PatientTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-003-C01: household and guarantor are separate links; invalid relationships are refused; every change is versioned, audited and in history.</summary>
public class PatientRelationshipTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private PatientTestSupport _s = null!;

    public async Task InitializeAsync() { await _fixture.InitializeAsync(); _s = new PatientTestSupport(_fixture); }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Patient> GuarantorAsync(Guid id, Guid? guarantorId)
    {
        var current = await _s.ReloadAsync(id);
        await using var db = _fixture.CreateContext();
        return await _s.Relationships(db).SetGuarantorAsync(id, guarantorId, Version(current), _s.Actor, default);
    }

    private async Task<Patient> JoinAsync(Guid id, Guid? anchorId, string? relationship = "Child")
    {
        var current = await _s.ReloadAsync(id);
        await using var db = _fixture.CreateContext();
        return await _s.Relationships(db).SetHouseholdAsync(id, anchorId, relationship, Version(current), _s.Actor, default);
    }

    private async Task<PatientDetail> DetailAsync(Guid id) { await using var db = _fixture.CreateContext(); return (await _s.Directory(db).GetDetailAsync(id, default))!; }

    private async Task<PatientException> RefusedAsync(Func<Task> action) => await Assert.ThrowsAsync<PatientException>(action);

    // ---------- Independence ----------

    [Fact]
    public async Task Household_and_guarantor_are_independent_a_guarantor_outside_the_household_and_members_with_different_guarantors()
    {
        var mom = await _s.RegisterAsync("Mia", "Lee", "1980-01-01", "(555) 010-0001");
        var kid = await _s.RegisterAsync("Cal", "Lee", "2015-01-01", "(555) 010-0002");
        var teen = await _s.RegisterAsync("Tia", "Lee", "2008-01-01", "(555) 010-0003");
        var grandpa = await _s.RegisterAsync("Gus", "Hale", "1950-01-01", "(555) 010-0004"); // lives elsewhere, pays for the kid

        await JoinAsync(kid.Id, mom.Id, "Child");   // household: mom (head), kid, teen
        await JoinAsync(teen.Id, mom.Id, "Child");
        await GuarantorAsync(kid.Id, grandpa.Id);   // guarantor OUTSIDE the household
        await GuarantorAsync(teen.Id, mom.Id);      // a different guarantor for another member

        var kidDetail = await DetailAsync(kid.Id);
        Assert.Equal(grandpa.Id, kidDetail.Guarantor!.Id);
        Assert.Equal(new[] { "Cal Lee", "Mia Lee", "Tia Lee" }, kidDetail.HouseholdMembers.Select(m => m.DisplayName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(kidDetail.HouseholdMembers, m => m.Id == grandpa.Id);

        var teenDetail = await DetailAsync(teen.Id);
        Assert.Equal(mom.Id, teenDetail.Guarantor!.Id);
        Assert.Equal(kidDetail.HouseholdId, teenDetail.HouseholdId);

        var momDetail = await DetailAsync(mom.Id);
        Assert.Null(momDetail.Guarantor);                       // responsible for themselves
        Assert.Equal("Self", momDetail.Patient.HouseholdRelationship);
        Assert.Equal(new[] { "Tia Lee" }, momDetail.GuaranteeFor.Select(p => p.DisplayName).ToArray());
        Assert.Equal(new[] { "Cal Lee" }, (await DetailAsync(grandpa.Id)).GuaranteeFor.Select(p => p.DisplayName).ToArray());
        Assert.Null((await DetailAsync(grandpa.Id)).HouseholdId); // a guarantor with no household
    }

    [Fact]
    public async Task A_household_can_exist_with_no_guarantor_links_and_a_guarantor_link_with_no_household()
    {
        var a = await _s.RegisterAsync("Ann", "Lee", "1980-01-01", "(555) 010-0001");
        var b = await _s.RegisterAsync("Ben", "Lee", "1982-01-01", "(555) 010-0002");
        await JoinAsync(b.Id, a.Id, "Spouse");
        Assert.Null((await DetailAsync(b.Id)).Guarantor);

        var c = await _s.RegisterAsync("Cy", "Moss", "1990-01-01", "(555) 010-0003");
        await GuarantorAsync(c.Id, a.Id);
        Assert.Null((await DetailAsync(c.Id)).HouseholdId);
    }

    // ---------- Guarantor rules ----------

    [Fact]
    public async Task A_guarantor_can_be_cleared_or_set_to_self_and_either_means_self_responsible()
    {
        var g = await _s.RegisterAsync("Gina", "Lee", "1960-01-01", "(555) 010-0001");
        var p = await _s.RegisterAsync("Cal", "Lee", "2015-01-01", "(555) 010-0002");
        await GuarantorAsync(p.Id, g.Id);

        var cleared = await GuarantorAsync(p.Id, null);
        Assert.Null(cleared.GuarantorPatientId);
        await GuarantorAsync(p.Id, g.Id);
        var self = await GuarantorAsync(p.Id, p.Id);
        Assert.Null(self.GuarantorPatientId);
    }

    [Fact]
    public async Task Setting_the_guarantor_it_already_has_changes_nothing()
    {
        var g = await _s.RegisterAsync("Gina", "Lee", "1960-01-01", "(555) 010-0001");
        var p = await _s.RegisterAsync("Cal", "Lee", "2015-01-01", "(555) 010-0002");
        var first = await GuarantorAsync(p.Id, g.Id);
        var again = await GuarantorAsync(p.Id, g.Id);

        Assert.Equal(Version(first), Version(again));
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.PatientHistory.CountAsync(h => h.ChangeType == "GuarantorChanged"));
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == PatientAuditEventsV2.GuarantorChanged));
    }

    [Fact]
    public async Task Invalid_guarantors_are_refused_with_invalid_relationship()
    {
        var g = await _s.RegisterAsync("Gina", "Lee", "1960-01-01", "(555) 010-0001");
        var p = await _s.RegisterAsync("Cal", "Lee", "2015-01-01", "(555) 010-0002");
        var other = await _s.RegisterAsync("Oz", "Kane", "1970-01-01", "(555) 010-0003");

        // a guarantor who does not exist
        Assert.Equal("invalid_relationship", (await RefusedAsync(() => GuarantorAsync(p.Id, Guid.NewGuid()))).Code);

        // an inactive guarantor
        var inactive = await _s.RegisterAsync("Ina", "Cole", "1965-01-01", "(555) 010-0004");
        await using (var db = _fixture.CreateContext()) await _s.Edits(db).SetActiveAsync(inactive.Id, false, Version(inactive), _s.Actor, default);
        Assert.Contains("inactive", (await RefusedAsync(() => GuarantorAsync(p.Id, inactive.Id))).Message);

        // a chain: the guarantor must be responsible for themselves
        await GuarantorAsync(other.Id, g.Id);
        Assert.Contains("guarantor of their own", (await RefusedAsync(() => GuarantorAsync(p.Id, other.Id))).Message);

        // and the reverse: someone who already guarantees others cannot be given a guarantor (this is what prevents a cycle)
        Assert.Contains("already the guarantor", (await RefusedAsync(() => GuarantorAsync(g.Id, p.Id))).Message);

        Assert.Null((await _s.ReloadAsync(p.Id)).GuarantorPatientId);
        Assert.Null((await _s.ReloadAsync(g.Id)).GuarantorPatientId);
    }

    // ---------- Household rules ----------

    [Fact]
    public async Task Joining_a_household_creates_it_for_the_anchor_and_records_the_relationship_for_both()
    {
        var anchor = await _s.RegisterAsync("Mia", "Lee", "1980-01-01", "(555) 010-0001");
        var kid = await _s.RegisterAsync("Cal", "Lee", "2015-01-01", "(555) 010-0002");

        var joined = await JoinAsync(kid.Id, anchor.Id, "Child");

        var anchorNow = await _s.ReloadAsync(anchor.Id);
        Assert.NotNull(joined.HouseholdId);
        Assert.Equal(joined.HouseholdId, anchorNow.HouseholdId);
        Assert.Equal("Child", joined.HouseholdRelationship);
        Assert.Equal("Self", anchorNow.HouseholdRelationship);
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.Households.CountAsync());
        Assert.Equal(2, await db.PatientHistory.CountAsync(h => h.ChangeType == "HouseholdChanged" && h.FieldName == "householdId"));
        var audits = await db.AuditLogEntries.Where(a => a.EventType == PatientAuditEventsV2.HouseholdChanged).ToListAsync();
        Assert.Equal(2, audits.Count);  // one for the member, one for the household's creation on the anchor
        Assert.All(audits, a => Assert.Equal(_s.Actor, a.PerformedByUserAccountId));
    }

    [Fact]
    public async Task A_third_member_joins_the_same_household_through_any_member_and_can_change_their_relationship_label()
    {
        var a = await _s.RegisterAsync("Mia", "Lee", "1980-01-01", "(555) 010-0001");
        var b = await _s.RegisterAsync("Cal", "Lee", "2015-01-01", "(555) 010-0002");
        var c = await _s.RegisterAsync("Tia", "Lee", "2008-01-01", "(555) 010-0003");
        await JoinAsync(b.Id, a.Id, "Child");
        var joined = await JoinAsync(c.Id, b.Id, "Sibling"); // anchored through b, lands in the same household

        Assert.Equal((await _s.ReloadAsync(a.Id)).HouseholdId, joined.HouseholdId);
        var relabelled = await JoinAsync(c.Id, a.Id, "Child");
        Assert.Equal("Child", relabelled.HouseholdRelationship);
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.Households.CountAsync());
    }

    [Fact]
    public async Task Leaving_a_household_clears_the_membership_and_removes_a_household_nobody_belongs_to()
    {
        var a = await _s.RegisterAsync("Mia", "Lee", "1980-01-01", "(555) 010-0001");
        var b = await _s.RegisterAsync("Cal", "Lee", "2015-01-01", "(555) 010-0002");
        await JoinAsync(b.Id, a.Id, "Child");

        var left = await JoinAsync(b.Id, null, null);
        Assert.Null(left.HouseholdId);
        Assert.Null(left.HouseholdRelationship);
        await using (var db = _fixture.CreateContext()) Assert.Equal(1, await db.Households.CountAsync()); // a is still in it

        await JoinAsync(a.Id, null, null);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(0, await verify.Households.CountAsync());
        Assert.Equal(2, await verify.Patients.CountAsync()); // patients are never removed
    }

    [Fact]
    public async Task Invalid_household_requests_are_refused()
    {
        var a = await _s.RegisterAsync("Mia", "Lee", "1980-01-01", "(555) 010-0001");
        var b = await _s.RegisterAsync("Cal", "Lee", "2015-01-01", "(555) 010-0002");
        var c = await _s.RegisterAsync("Zed", "Kane", "1975-01-01", "(555) 010-0003");
        var d = await _s.RegisterAsync("Dee", "Kane", "1977-01-01", "(555) 010-0004");

        Assert.Contains("own household", (await RefusedAsync(() => JoinAsync(a.Id, a.Id))).Message);            // themselves
        Assert.Equal("invalid_relationship", (await RefusedAsync(() => JoinAsync(a.Id, Guid.NewGuid()))).Code);  // nobody
        var bad = await RefusedAsync(() => JoinAsync(b.Id, a.Id, "Cousin-in-law"));                              // unknown label
        Assert.Equal("validation_failed", bad.Code);
        Assert.Contains("relationship", bad.FieldErrors.Keys);
        Assert.Equal("validation_failed", (await RefusedAsync(() => JoinAsync(b.Id, a.Id, null))).Code);         // missing label

        await JoinAsync(b.Id, a.Id, "Child");
        await JoinAsync(d.Id, c.Id, "Spouse");
        var different = await RefusedAsync(() => JoinAsync(b.Id, c.Id, "Child"));                               // already in another household
        Assert.Equal("already_in_household", different.Code);
        Assert.Equal(409, different.StatusCode);

        var inactive = await _s.RegisterAsync("Ina", "Cole", "1965-01-01", "(555) 010-0005");
        await using (var db = _fixture.CreateContext()) await _s.Edits(db).SetActiveAsync(inactive.Id, false, Version(inactive), _s.Actor, default);
        Assert.Contains("inactive", (await RefusedAsync(() => JoinAsync(a.Id, inactive.Id))).Message);

        Assert.Equal((await _s.ReloadAsync(a.Id)).HouseholdId, (await _s.ReloadAsync(b.Id)).HouseholdId); // b is still where they were
    }

    // ---------- Concurrency ----------

    [Fact]
    public async Task A_stale_relationship_change_is_a_conflict()
    {
        var g = await _s.RegisterAsync("Gina", "Lee", "1960-01-01", "(555) 010-0001");
        var g2 = await _s.RegisterAsync("Hal", "Lee", "1962-01-01", "(555) 010-0002");
        var p = await _s.RegisterAsync("Cal", "Lee", "2015-01-01", "(555) 010-0003");
        var stale = Version(p);
        await GuarantorAsync(p.Id, g.Id); // another user changes the patient first

        await using var db = _fixture.CreateContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _s.Relationships(db).SetGuarantorAsync(p.Id, g2.Id, stale, _s.Actor, default));
        Assert.Equal(g.Id, (await _s.ReloadAsync(p.Id)).GuarantorPatientId);

        await using var db2 = _fixture.CreateContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _s.Relationships(db2).SetHouseholdAsync(p.Id, g.Id, "Child", stale, _s.Actor, default));
    }

    [Fact]
    public async Task Relationship_changes_require_a_row_version_and_a_known_patient()
    {
        var p = await _s.RegisterAsync("Cal", "Lee", "2015-01-01", "(555) 010-0003");
        await using var db = _fixture.CreateContext();
        Assert.Equal("row_version_required", (await RefusedAsync(() => _s.Relationships(db).SetGuarantorAsync(p.Id, null, null, _s.Actor, default))).Code);
        Assert.Equal("patient_not_found", (await RefusedAsync(() => _s.Relationships(db).SetHouseholdAsync(Guid.NewGuid(), null, null, "AAAA", _s.Actor, default))).Code);
    }

    [Fact]
    public async Task Relationship_audit_entries_name_no_one()
    {
        var g = await _s.RegisterAsync("Gina", "Lee", "1960-01-01", "(555) 010-0001");
        var p = await _s.RegisterAsync("Cal", "Lee", "2015-01-01", "(555) 010-0003");
        await GuarantorAsync(p.Id, g.Id);
        await JoinAsync(p.Id, g.Id, "Child");

        await using var db = _fixture.CreateContext();
        var audits = await db.AuditLogEntries.Where(a => a.EventType == PatientAuditEventsV2.GuarantorChanged || a.EventType == PatientAuditEventsV2.HouseholdChanged).ToListAsync();
        Assert.NotEmpty(audits);
        Assert.All(audits, a => { Assert.DoesNotContain("Gina", a.Details); Assert.DoesNotContain("Cal", a.Details); Assert.DoesNotContain("Lee", a.Details); });
    }
}
