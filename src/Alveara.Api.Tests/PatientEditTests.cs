using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Patients;
using Xunit;
using static Alveara.Api.Tests.PatientTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-003-C01: safe edits with history and concurrency, and the active/inactive state.</summary>
public class PatientEditTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private PatientTestSupport _s = null!;

    public async Task InitializeAsync() { await _fixture.InitializeAsync(); _s = new PatientTestSupport(_fixture); }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Patient> UpdateAsync(Patient patient, Func<PatientFields, PatientFields> change, string? version = null)
    {
        await using var db = _fixture.CreateContext();
        return await _s.Edits(db).UpdateAsync(patient.Id, change(Fields(patient)), version ?? Version(patient), _s.Actor, default);
    }

    [Fact]
    public async Task An_edit_changes_only_what_was_changed_and_records_history_and_a_phi_free_audit_entry()
    {
        var patient = await _s.RegisterAsync("Ann", "Lee", "1985-03-09", "(555) 010-0100");

        var updated = await UpdateAsync(patient, f => f with { Phone = "(555) 222-3333", City = "Dallas" });

        Assert.Equal("(555) 222-3333", updated.Phone);
        Assert.Equal("Dallas", updated.City);
        Assert.Equal("Ann", updated.FirstName);
        Assert.Equal(_s.Actor, updated.UpdatedByUserId);
        Assert.NotNull(updated.UpdatedAtUtc);

        await using var db = _fixture.CreateContext();
        var history = await db.PatientHistory.Where(h => h.PatientId == patient.Id).OrderBy(h => h.FieldName).ToListAsync();
        Assert.Equal(new[] { "city", "phone" }, history.Select(h => h.FieldName).ToArray());
        var phone = history.Single(h => h.FieldName == "phone");
        Assert.Equal(("(555) 010-0100", "(555) 222-3333", "Updated", _s.Actor), (phone.OldValue, phone.NewValue, phone.ChangeType, phone.ChangedByUserId));
        var audit = await db.AuditLogEntries.SingleAsync(a => a.EventType == PatientAuditEventsV2.Updated);
        Assert.Equal(("Patient", _s.Actor, patient.Id), (audit.EntityType, audit.PerformedByUserAccountId, audit.TargetUserAccountId));
        Assert.Contains("phone", audit.Details);
        Assert.DoesNotContain("555", audit.Details);   // the audit log names fields, never values
        Assert.DoesNotContain("Dallas", audit.Details);
    }

    [Fact]
    public async Task Saving_with_nothing_changed_is_a_no_op_with_no_history_audit_or_version_bump()
    {
        var patient = await _s.RegisterAsync("Ann", "Lee");

        var same = await UpdateAsync(patient, f => f);

        Assert.Equal(Version(patient), Version(same));
        await using var db = _fixture.CreateContext();
        Assert.Equal(0, await db.PatientHistory.CountAsync());
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(a => a.EventType == PatientAuditEventsV2.Updated));
    }

    [Fact]
    public async Task A_stale_edit_is_rejected_and_never_overwrites_the_other_users_change()
    {
        var patient = await _s.RegisterAsync("Ann", "Lee");
        await UpdateAsync(patient, f => f with { Phone = "(555) 111-1111" }); // user A saves first

        // user B still holds the version they read before A saved
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => UpdateAsync(patient, f => f with { Phone = "(555) 222-2222" }));

        var stored = await _s.ReloadAsync(patient.Id);
        Assert.Equal("(555) 111-1111", stored.Phone);
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.PatientHistory.CountAsync()); // the rejected edit left no history either
    }

    [Fact]
    public async Task Two_simultaneous_edits_from_the_same_version_let_exactly_one_win()
    {
        var patient = await _s.RegisterAsync("Ann", "Lee");

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            try { await UpdateAsync(patient, f => f with { City = $"City{i}" }); return "saved"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        }));

        Assert.Equal(1, outcomes.Count(o => o == "saved"));
        Assert.Equal(5, outcomes.Count(o => o == "conflict"));
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.PatientHistory.CountAsync(h => h.FieldName == "city"));
    }

    [Theory]
    [InlineData(null, "row_version_required")]
    [InlineData("", "row_version_required")]
    [InlineData("not base64!", "row_version_invalid")]
    public async Task An_edit_without_a_usable_row_version_is_refused(string? version, string code)
    {
        var patient = await _s.RegisterAsync("Ann", "Lee");
        await using var db = _fixture.CreateContext();

        var ex = await Assert.ThrowsAsync<PatientException>(() => _s.Edits(db).UpdateAsync(patient.Id, Fields(patient), version, _s.Actor, default));

        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public async Task An_edit_that_breaks_a_required_field_names_it_and_changes_nothing()
    {
        var patient = await _s.RegisterAsync("Ann", "Lee");

        var ex = await Assert.ThrowsAsync<PatientException>(() => UpdateAsync(patient, f => f with { LastName = " ", Phone = "12" }));

        Assert.Equal("validation_failed", ex.Code);
        Assert.Equal(new[] { "lastName", "phone" }, ex.FieldErrors.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(Version(patient), Version(await _s.ReloadAsync(patient.Id)));
    }

    [Fact]
    public async Task Renaming_a_patient_into_another_patients_exact_identity_is_refused()
    {
        await _s.RegisterAsync("Ann", "Lee", "1985-03-09");
        var other = await _s.RegisterAsync("Ben", "Moss", "1985-03-09", "(555) 020-0200");

        var ex = await Assert.ThrowsAsync<PatientException>(() => UpdateAsync(other, f => f with { FirstName = "ann", LastName = " lee" }));

        Assert.Equal("duplicate_patient", ex.Code);
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("Ben", (await _s.ReloadAsync(other.Id)).FirstName);
    }

    [Fact]
    public async Task Correcting_a_name_updates_the_identity_key_so_the_old_identity_can_be_registered_again_and_the_new_one_cannot()
    {
        var patient = await _s.RegisterAsync("Anne", "Lee", "1985-03-09");
        await UpdateAsync(patient, f => f with { FirstName = "Ann" });

        await using var db = _fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<PatientRegistrationException>(() => _s.Registration(db).RegisterAsync(Request("Ann", "Lee", "1985-03-09"), "k-new", _s.Actor, default));
        Assert.Equal("duplicate_patient", ex.Code);
    }

    [Fact]
    public async Task Inactivating_a_patient_preserves_the_record_and_it_stays_inactive_across_later_edits()
    {
        var patient = await _s.RegisterAsync("Ann", "Lee");
        await using (var db = _fixture.CreateContext())
        {
            var inactive = await _s.Edits(db).SetActiveAsync(patient.Id, false, Version(patient), _s.Actor, default);
            Assert.False(inactive.IsActive);
            patient = inactive;
        }

        var edited = await UpdateAsync(patient, f => f with { City = "Houston" }, Version(patient));

        Assert.False(edited.IsActive); // an ordinary edit never silently reactivates
        Assert.Equal("Houston", edited.City);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.Patients.CountAsync()); // inactivated, never deleted
        var history = await verify.PatientHistory.Where(h => h.ChangeType == "Inactivated").ToListAsync();
        Assert.Equal("isActive", Assert.Single(history).FieldName);
        Assert.Equal(1, await verify.AuditLogEntries.CountAsync(a => a.EventType == PatientAuditEventsV2.StatusChanged));
    }

    [Fact]
    public async Task Reactivating_restores_the_patient_and_setting_the_same_state_again_is_a_no_op()
    {
        var patient = await _s.RegisterAsync("Ann", "Lee");
        Patient current;
        await using (var db = _fixture.CreateContext()) current = await _s.Edits(db).SetActiveAsync(patient.Id, false, Version(patient), _s.Actor, default);
        await using (var db = _fixture.CreateContext()) current = await _s.Edits(db).SetActiveAsync(patient.Id, true, Version(current), _s.Actor, default);
        Assert.True(current.IsActive);

        await using var again = _fixture.CreateContext();
        var repeat = await _s.Edits(again).SetActiveAsync(patient.Id, true, Version(current), _s.Actor, default);
        Assert.Equal(Version(current), Version(repeat));
        await using var verify = _fixture.CreateContext();
        Assert.Equal(2, await verify.PatientHistory.CountAsync()); // inactivated + activated, nothing for the repeat
    }

    [Fact]
    public async Task A_stale_status_change_is_a_conflict_and_a_guarantor_of_active_patients_cannot_be_inactivated()
    {
        var guarantor = await _s.RegisterAsync("Gina", "Lee", "1960-01-01", "(555) 010-0001");
        var child = await _s.RegisterAsync("Cal", "Lee", "2015-01-01", "(555) 010-0002");
        await using (var db = _fixture.CreateContext()) await _s.Relationships(db).SetGuarantorAsync(child.Id, guarantor.Id, Version(child), _s.Actor, default);
        guarantor = await _s.ReloadAsync(guarantor.Id);

        await using (var db = _fixture.CreateContext())
        {
            var ex = await Assert.ThrowsAsync<PatientException>(() => _s.Edits(db).SetActiveAsync(guarantor.Id, false, Version(guarantor), _s.Actor, default));
            Assert.Equal("guarantor_in_use", ex.Code);
            Assert.True((await _s.ReloadAsync(guarantor.Id)).IsActive);
        }

        await using (var db = _fixture.CreateContext())
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _s.Edits(db).SetActiveAsync(child.Id, false, Version(child), _s.Actor, default)); // child's version moved on when the guarantor was set
    }

    [Fact]
    public async Task Editing_or_inactivating_an_unknown_patient_returns_not_found()
    {
        await using var db = _fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<PatientException>(() => _s.Edits(db).UpdateAsync(Guid.NewGuid(), Request("A", "B", "2000-01-01").Fields, "AAAA", _s.Actor, default));
        Assert.Equal(404, ex.StatusCode);
        var ex2 = await Assert.ThrowsAsync<PatientException>(() => _s.Edits(db).SetActiveAsync(Guid.NewGuid(), false, "AAAA", _s.Actor, default));
        Assert.Equal("patient_not_found", ex2.Code);
    }
}
