using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-003 registration rules against a real SQL Server database: the three acceptance criteria
/// (captured details, prompts for required fields, audit with user + timestamp) and the five failure
/// paths (incomplete, duplicate, validation failure, audit failure, concurrency).
/// </summary>
public class PatientRegistrationServiceTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private static readonly IPracticeClock Clock = new PracticeClock(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));
    private readonly Guid _actor = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static RegisterPatientRequest Valid(string first = "Ann", string last = "Lee", string dob = "1985-03-09") =>
        new(first, "Marie", last, dob, "Female", "(555) 010-0100", "ann@example.test", "1 Main St", "Apt 2", "Austin", "TX", "78701");

    private Task<PatientRegistrationResult> RegisterAsync(RegisterPatientRequest request, string key)
    {
        var db = _fixture.CreateContext();
        return new PatientRegistrationService(db, Clock).RegisterAsync(request, key, _actor, default)
            .ContinueWith(t => { db.Dispose(); return t.GetAwaiter().GetResult(); });
    }

    private async Task<(int Patients, int Audits)> CountsAsync()
    {
        await using var db = _fixture.CreateContext();
        return (await db.Patients.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EventType == PatientAuditEvents.Registered));
    }

    // ---------- Acceptance 1: demographics and contact details are captured ----------

    [Fact]
    public async Task A_new_patient_is_captured_with_demographics_and_contact_details()
    {
        var result = await RegisterAsync(Valid(), "key-1");

        Assert.True(result.Created);
        await using var db = _fixture.CreateContext();
        var stored = await db.Patients.SingleAsync(p => p.Id == result.Patient.Id);
        Assert.Equal(("Ann", "Marie", "Lee"), (stored.FirstName, stored.MiddleName, stored.LastName));
        Assert.Equal(new DateOnly(1985, 3, 9), stored.DateOfBirth);
        Assert.Equal("Female", stored.Sex);
        Assert.Equal("(555) 010-0100", stored.Phone);
        Assert.Equal("ann@example.test", stored.Email);
        Assert.Equal(("1 Main St", "Apt 2", "Austin", "TX", "78701"), (stored.AddressLine1, stored.AddressLine2, stored.City, stored.State, stored.PostalCode));
    }

    [Fact]
    public async Task Surrounding_whitespace_is_trimmed_and_optional_fields_may_be_left_blank()
    {
        var request = Valid() with { FirstName = "  Ann  ", MiddleName = "  ", Email = "", Sex = null, AddressLine2 = null };
        var stored = (await RegisterAsync(request, "key-trim")).Patient;
        Assert.Equal("Ann", stored.FirstName);
        Assert.Null(stored.MiddleName);
        Assert.Null(stored.Email);
        Assert.Null(stored.AddressLine2);
    }

    // ---------- Acceptance 2: incomplete information prompts for required fields ----------

    [Fact]
    public async Task An_empty_registration_names_every_required_field_and_stores_nothing()
    {
        var ex = await Assert.ThrowsAsync<PatientRegistrationException>(() => RegisterAsync(new RegisterPatientRequest(null, null, null, null, null, null, null, null, null, null, null, null), "key-empty"));

        Assert.Equal("validation_failed", ex.Code);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(
            new[] { "addressLine1", "city", "dateOfBirth", "firstName", "lastName", "phone", "postalCode", "state" },
            ex.FieldErrors.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal((0, 0), await CountsAsync()); // no partial patient, and no audit entry for a registration that did not happen
    }

    [Fact]
    public async Task One_missing_field_is_reported_alone()
    {
        var ex = await Assert.ThrowsAsync<PatientRegistrationException>(() => RegisterAsync(Valid() with { Phone = "   " }, "key-nophone"));
        Assert.Equal(["phone"], ex.FieldErrors.Keys);
    }

    // ---------- Failure path: data validation failure ----------

    public static TheoryData<string, RegisterPatientRequest> InvalidInputs => new()
    {
        { "dateOfBirth", Valid() with { DateOfBirth = "03/09/1985" } },
        { "dateOfBirth", Valid() with { DateOfBirth = "1985-02-30" } },
        { "dateOfBirth", Valid() with { DateOfBirth = "2999-01-01" } },
        { "dateOfBirth", Valid() with { DateOfBirth = "1850-01-01" } },
        { "phone", Valid() with { Phone = "12-34" } },
        { "email", Valid() with { Email = "not-an-email" } },
        { "email", Valid() with { Email = "a@@b.com" } },
        { "firstName", Valid() with { FirstName = new string('x', 81) } },
        { "postalCode", Valid() with { PostalCode = new string('9', 21) } },
    };

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task Malformed_or_oversized_values_are_rejected_with_the_offending_field_named(string field, RegisterPatientRequest request)
    {
        var ex = await Assert.ThrowsAsync<PatientRegistrationException>(() => RegisterAsync(request, "key-invalid"));
        Assert.Equal(400, ex.StatusCode);
        Assert.Contains(field, ex.FieldErrors.Keys);
        Assert.Equal((0, 0), await CountsAsync());
    }

    [Fact]
    public async Task A_birth_date_of_today_is_accepted_and_the_maximum_length_is_accepted()
    {
        var today = DateOnly.FromDateTime(Clock.ToPracticeLocal(Clock.UtcNow).DateTime).ToString("yyyy-MM-dd");
        var result = await RegisterAsync(Valid(first: new string('A', 80), dob: today), "key-boundary");
        Assert.True(result.Created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_idempotency_key_is_refused_so_a_retry_can_never_double_register(string? key)
    {
        var db = _fixture.CreateContext();
        await using (db)
        {
            var ex = await Assert.ThrowsAsync<PatientRegistrationException>(() => new PatientRegistrationService(db, Clock).RegisterAsync(Valid(), key, _actor, default));
            Assert.Equal("idempotency_key_required", ex.Code);
        }
        Assert.Equal((0, 0), await CountsAsync());
    }

    // ---------- Failure path: duplicate entry ----------

    [Fact]
    public async Task Registering_the_same_person_again_is_refused_and_points_at_the_existing_record()
    {
        var first = (await RegisterAsync(Valid(), "key-a")).Patient;

        var ex = await Assert.ThrowsAsync<PatientRegistrationException>(() => RegisterAsync(Valid(first: "  ANN ", last: "lee"), "key-b"));

        Assert.Equal("duplicate_patient", ex.Code);
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(first.Id, ex.ExistingPatientId);
        Assert.Equal((1, 1), await CountsAsync());
    }

    [Fact]
    public async Task Different_people_with_the_same_name_but_different_birth_dates_are_both_registered()
    {
        await RegisterAsync(Valid(dob: "1985-03-09"), "key-father");
        await RegisterAsync(Valid(dob: "2012-07-21"), "key-son");
        Assert.Equal((2, 2), await CountsAsync());
    }

    // ---------- Idempotency: a retry is the same registration ----------

    [Fact]
    public async Task Retrying_with_the_same_key_returns_the_same_patient_without_a_second_row_or_audit_entry()
    {
        var original = await RegisterAsync(Valid(), "key-retry");
        var retry = await RegisterAsync(Valid(), "key-retry");

        Assert.True(original.Created);
        Assert.False(retry.Created);
        Assert.Equal(original.Patient.Id, retry.Patient.Id);
        Assert.Equal((1, 1), await CountsAsync());
    }

    // ---------- Failure path: concurrency ----------

    [Fact]
    public async Task Eight_simultaneous_registrations_of_the_same_person_create_exactly_one_patient_and_one_audit_entry()
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            try { return (await RegisterAsync(Valid(), $"key-race-{i}")).Created ? "created" : "replayed"; }
            catch (PatientRegistrationException ex) { return ex.Code; }
        }));

        Assert.Equal(1, attempts.Count(a => a == "created"));
        Assert.All(attempts.Where(a => a != "created"), a => Assert.Equal("duplicate_patient", a));
        Assert.Equal((1, 1), await CountsAsync());
    }

    [Fact]
    public async Task Simultaneous_retries_of_one_request_converge_on_one_patient()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RegisterAsync(Valid(), "key-same")));

        Assert.Single(results.Select(r => r.Patient.Id).Distinct());
        Assert.Equal(1, results.Count(r => r.Created));
        Assert.Equal((1, 1), await CountsAsync());
    }

    // ---------- Acceptance 3 (trust): audited with user and timestamp ----------

    [Fact]
    public async Task Registration_writes_an_audit_entry_with_the_user_and_timestamp_and_no_patient_details()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var patient = (await RegisterAsync(Valid(), "key-audit")).Patient;

        await using var db = _fixture.CreateContext();
        var entry = await db.AuditLogEntries.SingleAsync(a => a.EventType == PatientAuditEvents.Registered);
        Assert.Equal(_actor, entry.PerformedByUserAccountId);
        Assert.Equal(patient.Id, entry.TargetUserAccountId);
        Assert.Equal("Patient", entry.EntityType);
        Assert.InRange(entry.TimestampUtc, before, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.DoesNotContain("Ann", entry.Details);
        Assert.DoesNotContain("Lee", entry.Details);
        Assert.Equal(_actor, patient.CreatedByUserId);
    }

    // ---------- Failure path: audit failure ----------

    private sealed class FailAuditWrites : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditLogEntry>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("simulated audit storage failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task If_the_audit_write_fails_the_registration_is_not_stored_either()
    {
        var options = new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(_fixture.ConnectionString).AddInterceptors(new FailAuditWrites()).Options;
        await using (var failing = new AlveraDbContext(options))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => new PatientRegistrationService(failing, Clock).RegisterAsync(Valid(), "key-fail", _actor, default));
        }
        Assert.Equal((0, 0), await CountsAsync());

        // ...and the front desk can simply try again once audit storage is back.
        Assert.True((await RegisterAsync(Valid(), "key-fail")).Created);
        Assert.Equal((1, 1), await CountsAsync());
    }
}
