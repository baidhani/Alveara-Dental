using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N003: configuration CRUD/validation for practice, location, operatories, and appointment
/// types, against a real SQL Server database (unique indexes, filtered index, FK behavior and
/// rowversion all genuinely enforced - not an in-memory provider).
/// </summary>
public class PracticeConfigurationServiceTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private static readonly IPracticeClock Clock = new PracticeClock(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));
    private readonly Guid _actor = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private (AlveraDbContext Db, PracticeConfigurationService Service) NewService()
    {
        var db = _fixture.CreateContext();
        return (db, new PracticeConfigurationService(db, Clock));
    }

    private static string Version(byte[] v) => Convert.ToBase64String(v);

    private async Task<PracticeLocation> SeedLocationAsync(string name = "Main Office")
    {
        var (db, service) = NewService();
        await using (db) return await service.CreateLocationAsync(name, _actor, default);
    }

    // ---------- Practice ----------

    [Fact]
    public async Task Practice_first_save_creates_it_and_reflects_the_deployment_time_zone_and_currency_read_only()
    {
        var (db, service) = NewService();
        await using (db)
        {
            var before = await service.GetPracticeAsync(default);
            Assert.False(before.Configured);
            Assert.Equal("America/Chicago", before.TimeZoneId); // from IPracticeClock, not a database column
            Assert.Equal("USD", before.Currency);              // ALV-N002's fixed currency

            var saved = await service.SavePracticeAsync("Alveara Dental", "555-0100", "1 Main St", null, _actor, default);
            Assert.True(saved.Configured);
            Assert.Equal("Alveara Dental", saved.Name);
            Assert.Equal("America/Chicago", saved.TimeZoneId);
            Assert.Equal("USD", saved.Currency);
        }
    }

    [Fact]
    public async Task Practice_update_requires_the_version_and_a_stale_version_is_a_concurrency_conflict()
    {
        PracticeSettingsView created;
        var (db1, service1) = NewService();
        await using (db1) created = await service1.SavePracticeAsync("Alveara Dental", null, null, null, _actor, default);

        var (db2, service2) = NewService();
        await using (db2)
        {
            var missing = await Assert.ThrowsAsync<ConfigurationException>(() => service2.SavePracticeAsync("X", null, null, null, _actor, default));
            Assert.Equal("row_version_required", missing.Code);

            await service2.SavePracticeAsync("Renamed", null, null, created.RowVersion, _actor, default); // current version: succeeds
        }

        var (db3, service3) = NewService();
        await using (db3)
        {
            // created.RowVersion is now stale: someone else saved in between.
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service3.SavePracticeAsync("Stale edit", null, null, created.RowVersion, _actor, default));
            Assert.Equal("Renamed", (await service3.GetPracticeAsync(default)).Name);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Practice_name_is_required(string name)
    {
        var (db, service) = NewService();
        await using (db)
        {
            var ex = await Assert.ThrowsAsync<ConfigurationException>(() => service.SavePracticeAsync(name, null, null, null, _actor, default));
            Assert.Equal("name_required", ex.Code);
        }
    }

    // ---------- Location ----------

    [Fact]
    public async Task A_second_active_location_is_refused_by_the_service_and_backstopped_by_the_database()
    {
        await SeedLocationAsync("Main Office");

        var (db, service) = NewService();
        await using (db)
        {
            var ex = await Assert.ThrowsAsync<ConfigurationException>(() => service.CreateLocationAsync("Second Office", _actor, default));
            Assert.Equal("second_active_location_not_supported", ex.Code);
            Assert.Equal(409, ex.StatusCode);
        }

        // Bypass the service entirely: the filtered unique index must still make it impossible.
        await using var raw = _fixture.CreateContext();
        raw.PracticeLocations.Add(new PracticeLocation { Id = Guid.NewGuid(), Name = "Sneaky", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
    }

    [Fact]
    public async Task A_location_with_active_operatories_cannot_be_inactivated_and_an_inactive_one_is_still_resolvable()
    {
        var location = await SeedLocationAsync();
        var (db, service) = NewService();
        await using (db)
        {
            var operatory = await service.CreateOperatoryAsync("Op 1", _actor, default);

            var refused = await Assert.ThrowsAsync<ConfigurationException>(
                () => service.SetLocationActiveAsync(location.Id, false, Version(location.RowVersion), _actor, default));
            Assert.Equal("location_has_active_operatories", refused.Code);

            var op = await service.GetOperatoryAsync(operatory.Id, default);
            await service.SetOperatoryActiveAsync(op.Id, false, Version(op.RowVersion), _actor, default);
            var freshLocation = await service.GetLocationAsync(location.Id, default);
            var inactivated = await service.SetLocationActiveAsync(location.Id, false, Version(freshLocation.RowVersion), _actor, default);

            Assert.False(inactivated.IsActive);
            Assert.False((await service.GetLocationAsync(location.Id, default)).IsActive); // still resolvable by id
            Assert.DoesNotContain(await service.ListLocationsAsync(false, default), l => l.Id == location.Id);
            Assert.Contains(await service.ListLocationsAsync(true, default), l => l.Id == location.Id);
        }
    }

    // ---------- Operatories ----------

    [Fact]
    public async Task An_operatory_needs_an_active_location_and_a_unique_name()
    {
        var (db0, service0) = NewService();
        await using (db0)
        {
            var none = await Assert.ThrowsAsync<ConfigurationException>(() => service0.CreateOperatoryAsync("Op 1", _actor, default));
            Assert.Equal("no_active_location", none.Code);
        }

        await SeedLocationAsync();
        var (db, service) = NewService();
        await using (db)
        {
            await service.CreateOperatoryAsync("Op 1", _actor, default);
            var duplicate = await Assert.ThrowsAsync<ConfigurationException>(() => service.CreateOperatoryAsync("op 1", _actor, default)); // case-insensitive collation
            Assert.Equal("operatory_name_taken", duplicate.Code);
            Assert.Equal(409, duplicate.StatusCode);
        }
    }

    [Fact]
    public async Task An_inactivated_operatory_is_hidden_from_default_lists_but_still_resolvable_and_reactivatable()
    {
        await SeedLocationAsync();
        var (db, service) = NewService();
        await using (db)
        {
            var op = await service.CreateOperatoryAsync("Op 1", _actor, default);
            await service.SetOperatoryActiveAsync(op.Id, false, Version(op.RowVersion), _actor, default);

            Assert.DoesNotContain(await service.ListOperatoriesAsync(false, default), o => o.Id == op.Id);
            Assert.Contains(await service.ListOperatoriesAsync(true, default), o => o.Id == op.Id);
            var resolved = await service.GetOperatoryAsync(op.Id, default);
            Assert.False(resolved.IsActive);
            Assert.Equal("Op 1", resolved.Name);

            await service.SetOperatoryActiveAsync(op.Id, true, Version(resolved.RowVersion), _actor, default);
            Assert.True((await service.GetOperatoryAsync(op.Id, default)).IsActive);
        }
    }

    [Fact]
    public async Task Setting_an_operatory_to_the_state_it_already_has_is_a_harmless_no_op_with_no_duplicate_audit()
    {
        await SeedLocationAsync();
        var (db, service) = NewService();
        await using (db)
        {
            var op = await service.CreateOperatoryAsync("Op 1", _actor, default);
            var before = await db.AuditLogEntries.CountAsync(a => a.TargetUserAccountId == op.Id);
            await service.SetOperatoryActiveAsync(op.Id, true, null, _actor, default); // already active: idempotent
            Assert.Equal(before, await db.AuditLogEntries.CountAsync(a => a.TargetUserAccountId == op.Id));
        }
    }

    // ---------- Appointment types ----------

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(-5)]
    [InlineData(485)]
    public async Task Appointment_duration_outside_the_allowed_range_or_step_is_rejected(int minutes)
    {
        var (db, service) = NewService();
        await using (db)
        {
            var ex = await Assert.ThrowsAsync<ConfigurationException>(() => service.CreateAppointmentTypeAsync("Exam", minutes, _actor, default));
            Assert.Equal("invalid_duration", ex.Code);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(480)]
    public async Task Appointment_duration_at_the_boundaries_is_accepted(int minutes)
    {
        var (db, service) = NewService();
        await using (db)
        {
            var type = await service.CreateAppointmentTypeAsync($"Type {minutes}", minutes, _actor, default);
            Assert.Equal(minutes, type.DefaultDurationMinutes);
        }
    }

    [Fact]
    public async Task Appointment_types_have_unique_names_and_the_duration_change_is_audited()
    {
        var (db, service) = NewService();
        await using (db)
        {
            var type = await service.CreateAppointmentTypeAsync("Exam", 30, _actor, default);
            var duplicate = await Assert.ThrowsAsync<ConfigurationException>(() => service.CreateAppointmentTypeAsync("EXAM", 45, _actor, default));
            Assert.Equal("appointment_type_name_taken", duplicate.Code);

            await service.UpdateAppointmentTypeAsync(type.Id, "Exam", 45, Version(type.RowVersion), _actor, default);
            var audit = await db.AuditLogEntries.Where(a => a.TargetUserAccountId == type.Id).OrderBy(a => a.TimestampUtc).ToListAsync();
            Assert.Contains(audit, a => a.EventType == ConfigurationAuditEvents.Created && a.EntityType == "AppointmentType");
            Assert.Contains(audit, a => a.EventType == ConfigurationAuditEvents.Updated && a.Details.Contains("from 30 to 45"));
            Assert.All(audit, a => Assert.Equal(_actor, a.PerformedByUserAccountId));
        }
    }

    [Fact]
    public async Task A_configuration_change_and_its_audit_entry_commit_or_fail_together()
    {
        await SeedLocationAsync();
        var (db, service) = NewService();
        await using (db)
        {
            var op = await service.CreateOperatoryAsync("Op 1", _actor, default);

            // A stale edit must roll back BOTH the change and its audit entry.
            var stale = Version(op.RowVersion);
            var current = await service.GetOperatoryAsync(op.Id, default);
            await service.UpdateOperatoryAsync(op.Id, "Op 1 renamed", Version(current.RowVersion), _actor, default);
            var auditCountAfterSuccess = await db.AuditLogEntries.CountAsync(a => a.TargetUserAccountId == op.Id);

            await using var db2 = _fixture.CreateContext();
            var service2 = new PracticeConfigurationService(db2, Clock);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service2.UpdateOperatoryAsync(op.Id, "Stale rename", stale, _actor, default));

            await using var verify = _fixture.CreateContext();
            Assert.Equal("Op 1 renamed", (await verify.Operatories.SingleAsync(o => o.Id == op.Id)).Name);
            Assert.Equal(auditCountAfterSuccess, await verify.AuditLogEntries.CountAsync(a => a.TargetUserAccountId == op.Id));
        }
    }

    [Fact]
    public async Task Referenced_configuration_cannot_be_destructively_deleted_at_the_database()
    {
        await SeedLocationAsync();
        Guid operatoryLocationId;
        var (db, service) = NewService();
        await using (db)
        {
            var op = await service.CreateOperatoryAsync("Op 1", _actor, default);
            operatoryLocationId = op.LocationId;
        }

        await using var raw = _fixture.CreateContext();
        var location = await raw.PracticeLocations.SingleAsync(l => l.Id == operatoryLocationId);
        raw.PracticeLocations.Remove(location);
        await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync()); // FK is Restrict
    }

    [Fact]
    public void The_new_permission_is_held_by_the_practice_manager_and_admin_but_not_front_desk_or_clinical_roles()
    {
        Assert.True(PermissionMatrix.RoleHas(Role.OfficeManager, Permission.ManagePracticeConfiguration));
        Assert.True(PermissionMatrix.RoleHas(Role.Admin, Permission.ManagePracticeConfiguration));
        foreach (var role in new[] { Role.Dentist, Role.Hygienist, Role.Assistant, Role.FrontDesk, Role.Billing, Role.Unassigned })
            Assert.False(PermissionMatrix.RoleHas(role, Permission.ManagePracticeConfiguration), role.ToString());
    }
}
