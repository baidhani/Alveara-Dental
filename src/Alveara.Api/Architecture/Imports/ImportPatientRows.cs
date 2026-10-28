using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>A registered patient, with only what the duplicate comparison needs.</summary>
internal sealed record ImportExisting(Guid Id, DateOnly DateOfBirth, string FirstName, string LastName, string Phone, string? Email, string RegistrationKey);

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
            Cell("addressLine1"), Cell("addressLine2"), Cell("city"), Cell("state"), Cell("postalCode"));
    }

    /// <summary>The registered patients for a set of birth dates, in groups (one query per group of dates), at most <see cref="PoolCap"/> for each date as in the front desk's check.</summary>
    public static async Task<Dictionary<DateOnly, List<ImportExisting>>> LoadPoolsAsync(AlveraDbContext db, IReadOnlyList<DateOnly> dates, CancellationToken ct)
    {
        var pools = new Dictionary<DateOnly, List<ImportExisting>>();
        foreach (var chunk in dates.Chunk(ChunkSize))
        {
            var found = await db.Patients.AsNoTracking().Where(p => chunk.Contains(p.DateOfBirth)).OrderBy(p => p.Id)
                .Select(p => new ImportExisting(p.Id, p.DateOfBirth, p.FirstName, p.LastName, p.Phone, p.Email, p.RegistrationKey)).ToListAsync(ct);
            foreach (var group in found.GroupBy(p => p.DateOfBirth)) pools[group.Key] = group.Take(PoolCap).ToList();
        }

        return pools;
    }
}
