using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// ALV-018-C01: reads one patient's record from the database and turns it into the text of a <see cref="ReadableRecord"/>: dates formatted, staff and lookup names spelled out (a provider's display name, a procedure's
/// code and description, a document category's name) and every status as the system stores it. This is deliberately a SEPARATE function from the package's data files: the data files are the system's own rows for a
/// machine to read; this is the same record for a person. It is what the package calls for the readable record inside it, and what the stand-alone "readable record" download calls on its own. It reads, never writes,
/// and carries no secret; times are written in the practice's own time zone (see <see cref="PracticeTimeText"/>); a staff member appears by display name only, never by account. Failure modes: an unknown patient is a <see cref="PatientException"/> (404); a failing read is the database's exception to the caller.
/// A merged-away patient is not refused here (the caller decides); the record is read as it stands.
/// </summary>
public sealed partial class ReadableRecordLoader(AlveraDbContext db, IPracticeClock clock)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>One patient's record. <paramref name="providers"/> is the display names of the providers (<see cref="ProviderNamesAsync"/>); a caller making many records reads them once and passes them in.</summary>
    public async Task<ReadableRecord> LoadAsync(Guid patientId, CancellationToken ct, Dictionary<Guid, string>? providers = null)
    {
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == patientId, ct)
            ?? throw new PatientException("patient_not_found", "That patient was not found.", 404);
        var name = string.Join(" ", new[] { patient.FirstName, patient.MiddleName, patient.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var summary = new List<ReadableFact>
        {
            new("Name", name),
            new("Date of birth", patient.DateOfBirth.ToString("yyyy-MM-dd", Inv)),
            new("Sex", Or(patient.Sex)),
            new("Phone", patient.Phone),
            new("Email", Or(patient.Email)),
            new("Address", string.Join(", ", new[] { patient.AddressLine1, patient.AddressLine2, patient.City, $"{patient.State} {patient.PostalCode}".Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)))),
            new("Record status", patient.MergedAtUtc is not null ? $"Merged into another record on {Day(patient.MergedAtUtc.Value)}" : patient.IsActive ? "Active" : "Inactive"),
            new("Household relationship", Or(patient.HouseholdRelationship)),
            new("Registered", Day(patient.CreatedAtUtc)),
        };

        providers ??= await ProviderNamesAsync(ct);
        var sections = new List<ReadableSection>();
        sections.AddRange(await ClinicalSectionsAsync(patientId, ct));
        sections.AddRange(await PlanAndWorkSectionsAsync(patientId, providers, ct));
        sections.AddRange(await AdministrativeSectionsAsync(patientId, providers, ct));
        return new ReadableRecord(patientId, name, summary, sections);
    }

    public async Task<Dictionary<Guid, string>> ProviderNamesAsync(CancellationToken ct) =>
        await db.ProviderProfiles.AsNoTracking().Select(p => new { p.Id, p.StaffProfile!.DisplayName }).ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);

    private static string Provider(Dictionary<Guid, string> names, Guid? id) => id is not null && names.TryGetValue(id.Value, out var n) ? n : "";

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value;

    private string Day(DateTimeOffset t) => PracticeTimeText.Day(clock, t);

    private string Day(DateTimeOffset? t) => t is null ? "" : Day(t.Value);

    private string Moment(DateTimeOffset t) => PracticeTimeText.Moment(clock, t);

    private static string Day(DateOnly? d) => d?.ToString("yyyy-MM-dd", Inv) ?? "";

    private static string Money(decimal amount) => amount.ToString("0.00", Inv);

    /// <summary>The non-empty parts, in order, separated for reading ("Hives; Severe; 10 mg").</summary>
    private static string Parts(params string?[] parts) => string.Join("; ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string Tooth(string? tooth, string? surface) => string.IsNullOrWhiteSpace(tooth) ? "" : string.IsNullOrWhiteSpace(surface) ? tooth : $"{tooth} {surface}";

    private static IReadOnlyList<string> Row(params string?[] cells) => cells.Select(c => c ?? "").ToList();
}
