using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>Shared arrangement for ALV-003-C01's patient tests: every patient is created through the real registration service against real SQL Server.</summary>
public sealed class PatientTestSupport(TestDatabaseFixture fixture)
{
    public static readonly IPracticeClock Clock = new PracticeClock(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));
    public readonly Guid Actor = Guid.NewGuid();
    private int _n;

    public PatientRegistrationService Registration(AlveraDbContext db) => new(db, Clock, new PatientDuplicateDetector(db), new MeasurementEventSink(db));
    public PatientEditService Edits(AlveraDbContext db) => new(db, Clock);
    public PatientRelationshipService Relationships(AlveraDbContext db) => new(db, Clock);
    public PatientDirectory Directory(AlveraDbContext db) => new(db, Clock);

    public static RegisterPatientRequest Request(string first, string last, string dob, string phone = "(555) 010-0100", string? email = null, Guid[]? acknowledged = null) =>
        new(first, null, last, dob, "Female", phone, email, "1 Main St", null, "Austin", "TX", "78701", acknowledged);

    /// <summary>
    /// ALV-N025: with <paramref name="acknowledgeLikely"/>, a likely-duplicate warning (possible_duplicate) is acknowledged with a reason and the registration made,
    /// as the front desk does for a different person; an exact duplicate is never acknowledged (it cannot be). Off by default: every other caller is unchanged.
    /// </summary>
    public async Task<Patient> RegisterAsync(string first, string last, string dob = "1985-03-09", string phone = "(555) 010-0100", string? email = null, bool acknowledgeLikely = false)
    {
        var request = Request(first, last, dob, phone, email);
        if (acknowledgeLikely)
        {
            await using var check = fixture.CreateContext();
            var likely = await Registration(check).CheckDuplicatesAsync(request.Fields, default);
            if (likely.Count > 0 && likely.All(c => !c.Exact))
                request = request with { AcknowledgedDuplicateIds = likely.Select(c => c.Id).ToArray(), OverrideReason = "Test data: a different person" };
        }
        await using var db = fixture.CreateContext();
        var result = await Registration(db).RegisterAsync(request, $"seed-{Interlocked.Increment(ref _n)}-{Guid.NewGuid():N}", Actor, default);
        return result.Patient;
    }

    /// <summary>Re-reads a patient (with its current row version) in a fresh context.</summary>
    public async Task<Patient> ReloadAsync(Guid id)
    {
        await using var db = fixture.CreateContext();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.Patients), p => p.Id == id);
    }

    public static string Version(Patient p) => Convert.ToBase64String(p.RowVersion);

    public static PatientFields Fields(Patient p) => new(p.FirstName, p.MiddleName, p.LastName, p.DateOfBirth.ToString("yyyy-MM-dd"), p.Sex, p.Phone, p.Email, p.AddressLine1, p.AddressLine2, p.City, p.State, p.PostalCode);
}
