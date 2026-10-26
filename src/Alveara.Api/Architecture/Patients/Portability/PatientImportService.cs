using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Portability;

/// <summary>A file that was not imported because of what is in it. <see cref="Problems"/> says what to correct, each with a code, a place in the file and a message.</summary>
public sealed class PatientImportRejectedException(IReadOnlyList<PatientImportProblem> problems)
    : Exception("The file was not imported. Correct the problems listed and send it again.")
{
    public IReadOnlyList<PatientImportProblem> Problems { get; } = problems;
}

/// <summary>What a successful import gives back: the patient (new, or the one an earlier import of the same file created) and whether this call created it.</summary>
public sealed record PatientImportResult(Guid PatientId, bool Created, Guid ExportId);

/// <summary>
/// STORY-018: brings one patient in from an export file. Who may do it is decided by the permission on the controller. The order is deliberate:
/// 1. the whole file is checked first (<see cref="PatientImportValidator"/>); a file with any problem stores nothing, is logged as <c>PatientImportRejected</c> (the user and the time, the number of problems and
///    their codes, never a value from the file) and comes back as <see cref="PatientImportRejectedException"/> with every problem listed;
/// 2. a valid file is registered through the SAME registration service the front desk uses, so every protection of registration applies: the patient is created only if no exact or likely duplicate exists
///    (those are refused with the existing record or the candidates, and logged as rejected), and the registration key is derived from the file's export id, so importing the same file again returns the patient the
///    first import created and creates nothing;
/// 3. the registration and its <c>PatientImported</c> audit entry are one database transaction: an import can never create a patient without its log entry, and if the log entry cannot be saved the patient is
///    not created. An import only ever creates; it never overwrites, edits or merges an existing patient.
/// Failure modes handled: an empty, oversized, non-JSON, wrong or incomplete file; invalid fields; a duplicate; a repeat; a failing audit store (the call fails and nothing is stored). Not handled: more than one
/// patient per file, acknowledging likely duplicates from a file (the front-desk form does that). No external call is made, so no timeout or retry applies.
/// </summary>
public class PatientImportService(AlveraDbContext db, PatientRegistrationService registration, IPracticeClock clock)
{
    public const string EntityType = "PatientImport";

    public async Task<PatientImportResult> ImportAsync(byte[] content, Guid actor, CancellationToken ct)
    {
        var requirements = await PatientRegistrationSettingsService.LoadRequirementsAsync(db, ct);
        var check = PatientImportValidator.Validate(content, clock, requirements);
        if (check.Parsed is null)
        {
            await LogRejectedAsync(check.ExportId, check.FormatVersion, check.Problems.Select(p => p.Code), actor, ct);
            throw new PatientImportRejectedException(check.Problems);
        }

        var parsed = check.Parsed;
        var f = parsed.Fields;
        var request = new RegisterPatientRequest(f.FirstName, f.MiddleName, f.LastName, f.DateOfBirth, f.Sex, f.Phone, f.Email, f.AddressLine1, f.AddressLine2, f.City, f.State, f.PostalCode);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await registration.RegisterAsync(request, $"import-{parsed.ExportId:N}", actor, ct);
            AuditService.Record(db, AuditEventTypes.PatientImported, nameof(Patient), result.Patient.Id, actor,
                result.Created
                    ? $"Patient record imported from a file (format {PatientExportFormat.FormatName}, version {check.FormatVersion}, export {parsed.ExportId})."
                    : $"The same file (export {parsed.ExportId}) was imported again; the patient it created earlier was returned and nothing new was created.",
                null, parsed.ExportId);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new PatientImportResult(result.Patient.Id, result.Created, parsed.ExportId);
        }
        catch (PatientException ex)
        {
            await transaction.RollbackAsync(ct);                                                      // nothing of this call is kept
            db.ChangeTracker.Clear();
            await LogRejectedAsync(parsed.ExportId, check.FormatVersion, [ex.Code], actor, ct);       // a refused file is still an import action and is logged
            throw;
        }
    }

    /// <summary>Writes the log entry of a file that was not imported. It carries codes and counts only. If it cannot be saved the exception goes to the caller: a rejection that cannot be logged is a failure.</summary>
    private async Task LogRejectedAsync(Guid? exportId, int? formatVersion, IEnumerable<string> codes, Guid actor, CancellationToken ct)
    {
        var list = codes.ToList();
        var details = $"Patient import file not imported: {list.Count} problem(s) ({string.Join(", ", list.Distinct())})"
                      + (exportId is null ? "." : $"; export {exportId}.") + (formatVersion is null ? "" : $" Format version {formatVersion}.");
        AuditService.Record(db, AuditEventTypes.PatientImportRejected, EntityType, exportId ?? Guid.NewGuid(), actor, details, null, exportId);
        await db.SaveChangesAsync(ct);
    }
}
