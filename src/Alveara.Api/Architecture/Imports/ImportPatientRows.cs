using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>A registered patient, with only what the duplicate comparison needs.</summary>
/// ALV-N025: the address line 1 and postal code feed the different-birth-date rule.
internal sealed record ImportExisting(Guid Id, DateOnly DateOfBirth, string FirstName, string LastName, string Phone, string? Email, string? RegistrationKey, string? AddressLine1 = null, string? PostalCode = null);

/// <summary>
/// ALV-018-C02: what the dry-run and the commit share when they read a patient file: the stored file read back and proved to be the uploaded one, a row's cells turned into the front desk's fields through the
/// confirmed mapping, the registered patients with the same birth dates (in groups, never one query per row), and the registration key that ties a registered patient to its batch and row.
/// </summary>
internal static class ImportPatientRows
{
    private const int ChunkSize = 1_000, PoolCap = 500;

    /// <summary>The key a patient is registered under by an import. Stable for a batch and row, so registering the same row twice returns the patient the first time made. At most 46 characters.</summary>
    public static string RegistrationKey(Guid batchId, int rowNumber) => $"import-{batchId:N}-{rowNumber}";

    /// <summary>Reads the stored file back and proves it is the file that was uploaded: same bytes (SHA-256), still a sound table.</summary>
    public static async Task<CsvTable> ReadTableAsync(ImportFileStorage files, ImportBatch batch, ImportOptions options, CancellationToken ct)
    {
        byte[] content;
        try
        {
            await using var stream = await files.Blobs.OpenReadAsync(batch.FileBlobId, ct);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, ct);
            content = copy.ToArray();
        }
        catch (FileNotFoundException)
        {
            throw new ImportException("import_file_missing", "The stored import file cannot be found.", 409);
        }

        if (Convert.ToHexStringLower(SHA256.HashData(content)) != batch.FileSha256) throw new ImportException("import_file_changed", "The stored import file no longer matches the file that was uploaded.", 409);
        return CsvParser.Parse(content, options).Table ?? throw new ImportException("import_file_changed", "The stored import file is no longer a sound table.", 409);
    }

    /// <summary>Which column index fills each patient field, from the mapping of column name to field key.</summary>
    public static Dictionary<string, int> FieldIndex(CsvTable table, IReadOnlyDictionary<string, string> mapping)
    {
        var headers = table.Headers.ToList();
        return mapping.ToDictionary(kv => kv.Value, kv => headers.IndexOf(kv.Key));
    }

    public static PatientFields FieldsOf(string[] row, IReadOnlyDictionary<string, int> index)
    {
        string? Cell(string field) => index.TryGetValue(field, out var i) && i >= 0 ? row[i] : null;
        return new PatientFields(Cell("firstName"), Cell("middleName"), Cell("lastName"), Cell("dateOfBirth"), Cell("sex"), Cell("phone"), Cell("email"),
            Cell("addressLine1"), Cell("addressLine2"), Cell("city"), Cell("state"), Cell("postalCode"), Cell("country"));
    }

    /// <summary>The registered patients for a set of birth dates, in groups (one query per group of dates), at most <see cref="PoolCap"/> for each date as in the front desk's check.</summary>
    public static async Task<Dictionary<DateOnly, List<ImportExisting>>> LoadPoolsAsync(AlveraDbContext db, IReadOnlyList<DateOnly> dates, CancellationToken ct)
    {
        var pools = new Dictionary<DateOnly, List<ImportExisting>>();
        foreach (var chunk in dates.Chunk(ChunkSize))
        {
            var found = await db.Patients.AsNoTracking().Where(p => chunk.Contains(p.DateOfBirth)).OrderBy(p => p.Id)
                .Select(p => new ImportExisting(p.Id, p.DateOfBirth, p.FirstName, p.LastName, p.Phone, p.Email, p.RegistrationKey, p.AddressLine1, p.PostalCode)).ToListAsync(ct);
            foreach (var group in found.GroupBy(p => p.DateOfBirth)) pools[group.Key] = group.Take(PoolCap).ToList();
        }

        return pools;
    }

    /// <summary>
    /// ALV-N025 (F-145, F-210): the registered patients for a set of last names, in groups, keyed by the normalized last name, at most <see cref="PoolCap"/> per name -
    /// the candidates of the different-birth-date rule (the same bound as the front desk's second query).
    /// </summary>
    public static async Task<Dictionary<string, List<ImportExisting>>> LoadNamesakesAsync(AlveraDbContext db, IReadOnlyList<string> lastNames, CancellationToken ct)
    {
        var pools = new Dictionary<string, List<ImportExisting>>();
        foreach (var chunk in lastNames.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(ChunkSize))
        {
            var found = await db.Patients.AsNoTracking().Where(p => chunk.Contains(p.LastName)).OrderBy(p => p.Id)
                .Select(p => new ImportExisting(p.Id, p.DateOfBirth, p.FirstName, p.LastName, p.Phone, p.Email, p.RegistrationKey, p.AddressLine1, p.PostalCode)).ToListAsync(ct);
            foreach (var group in found.GroupBy(p => Patient.NormalizeName(p.LastName))) pools[group.Key] = group.Take(PoolCap).ToList();
        }

        return pools;
    }

    /// <summary>
    /// The registered patients a row matches, as the dry-run and the commit both judge them: the same-birth-date comparison over the birth-date pool, then (ALV-N025)
    /// the different-birth-date rule over the namesakes - which is never Exact, so such a row is a likely duplicate for a person to review, never "already registered".
    /// </summary>
    public static List<(ImportExisting Person, (bool Exact, List<string> Reasons) Match)> Matches(CleanPatient clean, DuplicateProbe probe,
        Dictionary<DateOnly, List<ImportExisting>> pools, Dictionary<string, List<ImportExisting>> namesakes)
    {
        var same = (pools.GetValueOrDefault(clean.DateOfBirth) ?? []).Select(p => (Person: p, Match: PatientDuplicateDetector.Compare(probe, p.FirstName, p.LastName, p.Phone, p.Email)));
        var other = (namesakes.GetValueOrDefault(probe.Last) ?? []).Where(p => p.DateOfBirth != clean.DateOfBirth)
            .Select(p => (Person: p, Match: (Exact: false, Reasons: PatientDuplicateDetector.CompareDifferentBirthDate(probe, p.FirstName, p.LastName, p.Phone, p.Email, p.AddressLine1, p.PostalCode))));
        return same.Concat(other).Where(m => m.Match.Reasons.Count > 0).ToList();
    }
}
