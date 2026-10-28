using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>A registered patient as an import file's row can name them.</summary>
internal sealed record ImportPatientRef(Guid Id, bool Active, bool Merged);

/// <summary>
/// ALV-018-C02 slice 5a: how a row of a file that is not a patient file names a patient: by first name, last name and date of birth, matched exactly (ignoring capitals and spacing) to ONE registered, active,
/// not merged-away patient. Shared by the appointment and the opening-balance reads so they can never match a person differently. Only the patients with the birth dates asked for are loaded, in groups.
/// </summary>
internal static class ImportPatientLookup
{
    private const int ChunkSize = 1_000;

    public static async Task<ILookup<string, ImportPatientRef>> LoadAsync(AlveraDbContext db, IReadOnlyList<DateOnly> dates, CancellationToken ct)
    {
        var all = new List<(string Key, ImportPatientRef Ref)>();
        foreach (var chunk in dates.Chunk(ChunkSize))
        {
            var found = await db.Patients.AsNoTracking().Where(p => chunk.Contains(p.DateOfBirth)).Select(p => new { p.Id, p.FirstName, p.LastName, p.DateOfBirth, p.IsActive, Merged = p.MergedAtUtc != null }).ToListAsync(ct);
            all.AddRange(found.Select(p => (Patient.BuildDuplicateKey(p.FirstName, p.LastName, p.DateOfBirth), new ImportPatientRef(p.Id, p.IsActive, p.Merged))));
        }

        return all.ToLookup(p => p.Key, p => p.Ref);
    }

    /// <summary>The one patient with this name and birth date, or null with a fixed message added to <c>errors["patient"]</c> (none, more than one, merged away, or inactive). Nothing is added when a name part is already a problem.</summary>
    public static ImportPatientRef? Resolve(string first, string last, DateOnly dob, ILookup<string, ImportPatientRef> patients, Dictionary<string, string> errors,
        string firstField = "patientFirstName", string lastField = "patientLastName", string dobField = "patientDateOfBirth")
    {
        if (errors.ContainsKey(firstField) || errors.ContainsKey(lastField) || errors.ContainsKey(dobField)) return null;
        var found = patients[Patient.BuildDuplicateKey(first, last, dob)].ToList();
        if (found.Count == 0) { errors["patient"] = "No registered patient has this name and date of birth. Import or register the patient first."; return null; }
        if (found.Count > 1) { errors["patient"] = "More than one registered patient has this name and date of birth."; return null; }
        if (found[0].Merged) { errors["patient"] = "This patient's record was merged into another one."; return null; }
        if (!found[0].Active) { errors["patient"] = "This patient is inactive."; return null; }
        return found[0];
    }
}
