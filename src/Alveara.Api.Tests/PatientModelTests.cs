using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>STORY-003 step 1: the Patient model, its duplicate key, its concurrency token and its permission grant.</summary>
public class PatientModelTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static Patient NewPatient(string first = "Ann", string last = "Lee", string dob = "1985-03-09") => new()
    {
        Id = Guid.NewGuid(),
        FirstName = first,
        LastName = last,
        DateOfBirth = DateOnly.Parse(dob),
        Phone = "555-0100",
        AddressLine1 = "1 Main St",
        City = "Austin",
        State = "TX",
        PostalCode = "78701",
        DuplicateKey = Patient.BuildDuplicateKey(first, last, DateOnly.Parse(dob)),
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Demographics_and_contact_details_round_trip_including_a_date_only_birth_date()
    {
        var patient = NewPatient();
        patient.MiddleName = "Marie";
        patient.Email = "ann@example.test";
        patient.Sex = "Female";
        await using (var db = _fixture.CreateContext()) { db.Patients.Add(patient); await db.SaveChangesAsync(); }

        await using var verify = _fixture.CreateContext();
        var stored = await verify.Patients.SingleAsync(p => p.Id == patient.Id);
        Assert.Equal(new DateOnly(1985, 3, 9), stored.DateOfBirth);
        Assert.Equal("Marie", stored.MiddleName);
        Assert.Equal("ann@example.test", stored.Email);
        Assert.Equal("555-0100", stored.Phone);
        Assert.Equal("78701", stored.PostalCode);
        Assert.NotEmpty(stored.RowVersion);
    }

    [Theory]
    [InlineData("Ann", "Lee", "  ann ", "LEE")]
    [InlineData("Mary  Jo", "O'Neil", "mary jo", " o'neil ")]
    public void Duplicate_key_ignores_case_and_extra_whitespace(string f1, string l1, string f2, string l2)
    {
        var dob = new DateOnly(2000, 1, 2);
        Assert.Equal(Patient.BuildDuplicateKey(f1, l1, dob), Patient.BuildDuplicateKey(f2, l2, dob));
    }

    [Fact]
    public void Duplicate_key_distinguishes_a_different_birth_date_or_name()
    {
        var a = Patient.BuildDuplicateKey("Ann", "Lee", new DateOnly(1985, 3, 9));
        Assert.NotEqual(a, Patient.BuildDuplicateKey("Ann", "Lee", new DateOnly(1985, 3, 10)));
        Assert.NotEqual(a, Patient.BuildDuplicateKey("Anne", "Lee", new DateOnly(1985, 3, 9)));
    }

    [Fact]
    public async Task The_database_rejects_a_second_patient_with_the_same_duplicate_key_even_when_the_service_check_is_bypassed()
    {
        await using (var db = _fixture.CreateContext()) { db.Patients.Add(NewPatient()); await db.SaveChangesAsync(); }

        await using var second = _fixture.CreateContext();
        second.Patients.Add(NewPatient(first: " ANN ", last: "lee"));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_stale_save_of_the_same_patient_is_rejected_not_silently_overwritten()
    {
        var patient = NewPatient();
        await using (var db = _fixture.CreateContext()) { db.Patients.Add(patient); await db.SaveChangesAsync(); }

        await using var a = _fixture.CreateContext();
        await using var b = _fixture.CreateContext();
        var asReadByA = await a.Patients.SingleAsync(p => p.Id == patient.Id);
        var asReadByB = await b.Patients.SingleAsync(p => p.Id == patient.Id);

        asReadByA.Phone = "555-1111";
        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(a, "Patient", patient.Id);
        asReadByB.Phone = "555-2222";
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => ConcurrencySaveGuard.SaveOrThrowConflictAsync(b, "Patient", patient.Id));

        await using var verify = _fixture.CreateContext();
        Assert.Equal("555-1111", (await verify.Patients.SingleAsync(p => p.Id == patient.Id)).Phone);
    }

    [Theory]
    [InlineData(Role.FrontDesk, true)]
    [InlineData(Role.OfficeManager, true)]
    [InlineData(Role.Admin, true)]
    [InlineData(Role.Dentist, false)]
    [InlineData(Role.Hygienist, false)]
    [InlineData(Role.Assistant, false)]
    [InlineData(Role.Billing, false)]
    [InlineData(Role.Unassigned, false)]
    public void Only_front_desk_office_manager_and_admin_may_register_patients(Role role, bool expected) =>
        Assert.Equal(expected, PermissionMatrix.RoleHas(role, Permission.RegisterPatients));
}
