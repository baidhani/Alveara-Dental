using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Patients;
using Xunit;
using static Alveara.Api.Tests.PatientTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-003-C01: "likely duplicate is warned without silent merge". Exact duplicates stay STORY-003's hard refusal; likely
/// duplicates are shown, must each be acknowledged to register anyway, and are never merged or altered.
/// </summary>
public class PatientDuplicateWarningTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private PatientTestSupport _s = null!;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(() => { _s = new PatientTestSupport(_fixture); return Task.CompletedTask; });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<PatientRegistrationException> RefusedAsync(RegisterPatientRequest request, string key = "k")
    {
        await using var db = _fixture.CreateContext();
        return await Assert.ThrowsAsync<PatientRegistrationException>(() => _s.Registration(db).RegisterAsync(request, key, _s.Actor, default));
    }

    private async Task<PatientRegistrationResult> RegisterAsync(RegisterPatientRequest request, string key)
    {
        await using var db = _fixture.CreateContext();
        return await _s.Registration(db).RegisterAsync(request, key, _s.Actor, default);
    }

    private async Task<int> PatientCountAsync() { await using var db = _fixture.CreateContext(); return await db.Patients.CountAsync(); }

    [Theory]
    [InlineData("Anna", "Lee", "(555) 999-0000", null, "Same last name and date of birth")]       // twin / first-name typo
    [InlineData("Ann", "Lea", "(555) 999-0000", null, "Same first name and date of birth, similar last name")] // last-name typo
    [InlineData("Zoe", "Park", "555.010.0100", null, "Same phone number and date of birth")]      // phone formatted differently
    [InlineData("Zoe", "Park", "(555) 999-0000", "ann@example.test", "Same email and date of birth")]
    public async Task A_likely_duplicate_is_warned_with_the_reason_and_nothing_is_registered(string first, string last, string phone, string? email, string reason)
    {
        var existing = await _s.RegisterAsync("Ann", "Lee", "1985-03-09", "(555) 010-0100", "ann@example.test");

        var refused = await RefusedAsync(Request(first, last, "1985-03-09", phone, email));

        Assert.Equal("possible_duplicate", refused.Code);
        Assert.Equal(409, refused.StatusCode);
        var candidate = Assert.Single(refused.Candidates);
        Assert.Equal(existing.Id, candidate.Id);
        Assert.False(candidate.Exact);
        Assert.Contains(reason, candidate.Reasons);
        Assert.Equal("Ann", candidate.FirstName); // the comparison panel carries the existing record's details
        Assert.Equal(1, await PatientCountAsync());
    }

    [Fact]
    public async Task Acknowledging_every_candidate_registers_the_new_patient_and_leaves_the_existing_one_untouched()
    {
        var existing = await _s.RegisterAsync("Ann", "Lee", "1985-03-09");
        var twin = Request("Anna", "Lee", "1985-03-09", phone: "(555) 777-0000");
        var warned = await RefusedAsync(twin);

        var result = await RegisterAsync(twin with { AcknowledgedDuplicateIds = warned.Candidates.Select(c => c.Id).ToArray(), OverrideReason = "Different person: twin sibling" }, "k-2");

        Assert.True(result.Created);
        Assert.Equal(2, await PatientCountAsync());
        var after = await _s.ReloadAsync(existing.Id);
        Assert.Equal(Version(existing), Version(after)); // no silent merge, no edit of the other record
        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.SingleAsync(a => a.TargetUserAccountId == result.Patient.Id);
        Assert.Contains("after reviewing possible duplicates", audit.Details);
        Assert.Contains(existing.Id.ToString(), audit.Details);                                    // ALV-N025 (D-02a): the acknowledged ids ...
        Assert.Equal("Different person: twin sibling", audit.Reason);                              // ... and the reason
        Assert.DoesNotContain("Lee", audit.Details);                                               // still no patient name or contact data
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Registering_anyway_without_a_reason_is_refused_and_nothing_is_registered(string? reason)
    {
        await _s.RegisterAsync("Ann", "Lee", "1985-03-09");
        var twin = Request("Anna", "Lee", "1985-03-09", phone: "(555) 777-0000");
        var warned = await RefusedAsync(twin);

        var refused = await RefusedAsync(twin with { AcknowledgedDuplicateIds = warned.Candidates.Select(c => c.Id).ToArray(), OverrideReason = reason }, "k-2");

        Assert.Equal(("override_reason_required", 400), (refused.Code, refused.StatusCode));
        Assert.True(refused.FieldErrors!.ContainsKey("overrideReason"));
        Assert.Equal(1, await PatientCountAsync());
    }

    [Fact]
    public async Task An_overlong_reason_is_refused_and_one_at_the_limit_is_kept()
    {
        await _s.RegisterAsync("Ann", "Lee", "1985-03-09");
        var twin = Request("Anna", "Lee", "1985-03-09", phone: "(555) 777-0000");
        var ack = (await RefusedAsync(twin)).Candidates.Select(c => c.Id).ToArray();

        var tooLong = await RefusedAsync(twin with { AcknowledgedDuplicateIds = ack, OverrideReason = new string('r', PatientRegistrationService.MaxOverrideReasonLength + 1) }, "k-2");
        Assert.Equal("override_reason_required", tooLong.Code);
        Assert.True((await RegisterAsync(twin with { AcknowledgedDuplicateIds = ack, OverrideReason = new string('r', PatientRegistrationService.MaxOverrideReasonLength) }, "k-3")).Created);
    }

    [Theory]
    [InlineData("(555) 010-0100", null, "9 Oak Ave", "Same name and phone number, different birth date")]
    [InlineData("(555) 999-0000", "carla@example.test", "9 Oak Ave", "Same name and email, different birth date")]
    [InlineData("(555) 999-0000", null, "1 MAIN ST", "Same name and address, different birth date")]
    public async Task The_same_name_with_a_different_birth_date_and_a_shared_contact_detail_is_warned_never_refused(string phone, string? email, string line1, string reason)
    {
        await using (var db = _fixture.CreateContext())
            await _s.Registration(db).RegisterAsync(Request("Carla", "Chen", "1992-04-02", "(555) 010-0100", "carla@example.test"), "k-seed", _s.Actor, default);
        var probe = Request("Carla", "Chen", "1997-04-02", phone, email) with { AddressLine1 = line1 };

        var refused = await RefusedAsync(probe);

        Assert.Equal(("possible_duplicate", 409), (refused.Code, refused.StatusCode));               // a warning the desk may pass with a reason, not the exact-duplicate refusal
        var candidate = Assert.Single(refused.Candidates);
        Assert.False(candidate.Exact);
        Assert.Equal([reason], candidate.Reasons);
        Assert.True((await RegisterAsync(probe with { AcknowledgedDuplicateIds = [candidate.Id], OverrideReason = "Mother and daughter" }, "k-2")).Created);
    }

    [Fact]
    public async Task The_same_name_with_a_different_birth_date_and_no_shared_contact_detail_registers_without_a_warning()
    {
        await using (var db = _fixture.CreateContext())
            await _s.Registration(db).RegisterAsync(Request("Carla", "Chen", "1992-04-02", "(555) 010-0100", "carla@example.test"), "k-seed", _s.Actor, default);
        var result = await RegisterAsync(Request("Carla", "Chen", "1997-04-02", "(555) 999-0000") with { AddressLine1 = "9 Oak Ave" }, "k-2");
        Assert.True(result.Created);
    }

    [Fact]
    public async Task A_partial_or_stale_acknowledgement_is_not_enough()
    {
        var a = await _s.RegisterAsync("Ann", "Lee", "1985-03-09", "(555) 010-0100");
        var b = await _s.RegisterAsync("Anna", "Leo", "1985-03-09", "(555) 020-0200"); // not a likely match for Ann Lee, but one for the next entry (same first name, last name one typo away)
        var request = Request("Anna", "Lee", "1985-03-09", phone: "(555) 777-0000");

        var refused = await RefusedAsync(request with { AcknowledgedDuplicateIds = [a.Id] });
        Assert.Equal("possible_duplicate", refused.Code);
        Assert.Equal(2, refused.Candidates.Count);

        var unknown = await RefusedAsync(request with { AcknowledgedDuplicateIds = [Guid.NewGuid()] });
        Assert.Equal("possible_duplicate", unknown.Code);

        Assert.True((await RegisterAsync(request with { AcknowledgedDuplicateIds = [a.Id, b.Id], OverrideReason = "Different person: checked ID" }, "k-ok")).Created);
    }

    [Fact]
    public async Task An_exact_duplicate_is_refused_even_if_the_user_acknowledges_it()
    {
        var existing = await _s.RegisterAsync("Ann", "Lee", "1985-03-09");

        var refused = await RefusedAsync(Request("ann", " LEE ", "1985-03-09", acknowledged: [existing.Id]));

        Assert.Equal("duplicate_patient", refused.Code);
        Assert.Equal(existing.Id, refused.ExistingPatientId);
        Assert.True(Assert.Single(refused.Candidates).Exact);
        Assert.Equal(1, await PatientCountAsync());
    }

    [Fact]
    public async Task Relatives_who_share_a_surname_phone_email_or_address_are_not_warned_when_their_birth_dates_differ()
    {
        await _s.RegisterAsync("Ann", "Lee", "1985-03-09", "(555) 010-0100", "family@example.test");

        // A spouse sharing surname, phone, email and address: a normal family, no warning at all.
        Assert.True((await RegisterAsync(Request("Ben", "Lee", "1983-01-15", "(555) 010-0100", "family@example.test"), "k-spouse")).Created);
        // STORY-003's father/son case: ALV-N025 (F-145, approved brief S3) - the SAME full name with a shared contact detail is now a warning (it may be one person
        // typed with a wrong year); it is still never refused or merged: the desk registers the son after saying why.
        var son = Request("Ann", "Lee", "2012-07-21", "(555) 010-0100", "family@example.test");
        var warned = await RefusedAsync(son, "k-son");
        Assert.Equal("possible_duplicate", warned.Code);
        Assert.False(Assert.Single(warned.Candidates).Exact);
        Assert.True((await RegisterAsync(son with { AcknowledgedDuplicateIds = [warned.Candidates[0].Id], OverrideReason = "Father and son" }, "k-son")).Created);
        Assert.Equal(3, await PatientCountAsync());
    }

    [Fact]
    public async Task Check_before_create_returns_candidates_without_registering_anything()
    {
        var existing = await _s.RegisterAsync("Ann", "Lee", "1985-03-09");
        await using var db = _fixture.CreateContext();

        var candidates = await _s.Registration(db).CheckDuplicatesAsync(Request("Anna", "Lee", "1985-03-09").Fields, default);

        Assert.Equal(existing.Id, Assert.Single(candidates).Id);
        Assert.Equal(1, await PatientCountAsync());
        var invalid = await Assert.ThrowsAsync<PatientRegistrationException>(() => _s.Registration(db).CheckDuplicatesAsync(Request("", "Lee", "1985-03-09").Fields, default));
        Assert.Contains("firstName", invalid.FieldErrors.Keys); // the same field rules apply to the early check
    }

    [Fact]
    public async Task Inactive_patients_are_still_shown_as_candidates_and_flagged_inactive()
    {
        var existing = await _s.RegisterAsync("Ann", "Lee", "1985-03-09");
        await using (var db = _fixture.CreateContext()) await _s.Edits(db).SetActiveAsync(existing.Id, false, Version(existing), _s.Actor, default);

        var refused = await RefusedAsync(Request("Anna", "Lee", "1985-03-09", phone: "(555) 777-0000"));

        Assert.False(Assert.Single(refused.Candidates).IsActive);
    }

    [Fact]
    public async Task Duplicate_detection_outcomes_are_recorded_as_privacy_safe_measurement_events()
    {
        await _s.RegisterAsync("Ann", "Lee", "1985-03-09");
        var twin = Request("Anna", "Lee", "1985-03-09", phone: "(555) 777-0000");
        var warned = await RefusedAsync(twin, "k-a");
        await RegisterAsync(twin with { AcknowledgedDuplicateIds = warned.Candidates.Select(c => c.Id).ToArray(), OverrideReason = "Different person" }, "k-b");
        await RefusedAsync(Request("Ann", "Lee", "1985-03-09"), "k-c");

        await using var db = _fixture.CreateContext();
        var events = await db.MeasurementEvents.Where(e => e.EventName == "patient.duplicate-check").ToListAsync();
        Assert.Equal(new[] { "exact_refused", "likely_overridden", "likely_warned" },
            events.Select(e => System.Text.Json.JsonDocument.Parse(e.PropertiesJson).RootElement.GetProperty("category").GetString()!).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.All(events, e =>
        {
            Assert.Equal(1, e.SchemaVersion);
            Assert.DoesNotContain("Ann", e.PropertiesJson); // counts and categories only - never a name
            Assert.DoesNotContain("Lee", e.PropertiesJson);
        });
    }

    [Fact]
    public async Task Eight_simultaneous_registrations_of_two_likely_twins_never_merge_and_never_create_an_exact_duplicate()
    {
        await _s.RegisterAsync("Ann", "Lee", "1985-03-09");
        var twin = Request("Anna", "Lee", "1985-03-09", phone: "(555) 777-0000");
        var ack = (await RefusedAsync(twin)).Candidates.Select(c => c.Id).ToArray();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            try { return (await RegisterAsync(twin with { AcknowledgedDuplicateIds = ack, OverrideReason = "Different person" }, $"k-twin-{i}")).Created ? "created" : "replayed"; }
            catch (PatientRegistrationException ex) { return ex.Code; }
        }));

        Assert.Equal(1, outcomes.Count(o => o == "created"));
        Assert.All(outcomes.Where(o => o != "created"), o => Assert.Contains(o, new[] { "duplicate_patient", "possible_duplicate" }));
        Assert.Equal(2, await PatientCountAsync());
    }
}
