using System.Globalization;
using System.Text.Json;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Prescribing;

/// <summary>What a prescription issue is: a print of an Active prescription, or a record copy (any status; an inactive one carries the watermark).</summary>
public static class PrescriptionIssueKinds
{
    public const string Print = "Print";
    public const string RecordCopy = "RecordCopy";
    public static readonly IReadOnlyList<string> All = [Print, RecordCopy];
}

/// <summary>
/// ALV-N026 S6 (D1, D2): one issue of a prescription as a PDF. It is append-only (the database refuses any change or deletion) and is owned through its prescription only: it carries no
/// <c>PatientId</c> (ownership decision), so patient scope, access and merge follow the prescription. No PDF is stored. A reprint is rebuilt from the immutable prescription and its detail, the
/// status the issue recorded (with the date and reason of <see cref="EventCutoffId"/> when that event ended it), and <see cref="EnvelopeJson"/>: the identity, prescriber and practice text printed then.
/// </summary>
public class PrescriptionIssue
{
    public Guid Id { get; set; }
    public Guid PrescriptionId { get; set; }
    /// <summary>One of <see cref="PrescriptionIssueKinds"/>.</summary>
    public required string IssueKind { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
    public Guid IssuedByUserId { get; set; }
    /// <summary>The template version, "p2" for every issue made here.</summary>
    public required string TemplateVersion { get; set; }
    /// <summary>One of <see cref="PrescriptionStatuses"/>, as it was at issue.</summary>
    public required string StatusAtIssue { get; set; }
    /// <summary>The last prescription event at issue (the ending event when the prescription had ended), or null when it had none.</summary>
    public Guid? EventCutoffId { get; set; }
    /// <summary>The printed identity, prescriber and practice text (<see cref="PrescriptionIssueEnvelope"/>). Displayed history, not ownership.</summary>
    public required string EnvelopeJson { get; set; }
    public string? RenderBundleId { get; set; }
    /// <summary>Optional diagnostic (D1); never an acceptance gate and never compared on reprint.</summary>
    public string? ContentHash { get; set; }
    /// <summary>Unique per prescription: a retried issue returns the first one.</summary>
    public required string IdempotencyKey { get; set; }
    public Guid CorrelationId { get; set; }
}

public sealed record IssuePatient(string Name, string DateOfBirth, long Mrn, string AddressLine1, string? AddressLine2, string City, string State, string PostalCode, string Country);
/// <summary>The prescriber as the staff profile said at issue. Optional fields are null when absent and are then left off the page (no empty labels, never a refusal).</summary>
public sealed record IssuePrescriber(string Name, string? Credential, string? LicenseNumber, string? LicenseJurisdiction, string? Npi);
public sealed record IssuePractice(string Name, string? Phone, string? AddressLine);
public sealed record PrescriptionIssueEnvelope(IssuePatient Patient, IssuePrescriber Prescriber, IssuePractice? Practice);

/// <summary>The answer to issuing: the issue, and whether it already existed for that key.</summary>
public sealed record IssuedPrescription(PrescriptionIssue Issue, bool Replayed);

/// <summary>
/// ALV-N026 S6: issues a prescription (a print or a record copy) and rebuilds an issue for its PDF. Only a missing patient address refuses (<c>print_incomplete</c>); a prescription saved before
/// instructions, quantity and refills has nothing complete to print (<c>print_incomplete</c> too). A print needs an Active prescription (<c>prescription_not_active</c>: issue a record copy instead).
/// </summary>
public class PrescriptionIssueService(AlveraDbContext db, PrescriptionRecordReader records, IPracticeClock clock, ILogger<PrescriptionIssueService> logger)
{
    public const string TemplateVersion = "p2";

    public async Task<IssuedPrescription> IssueAsync(Guid prescriptionId, string? kind, string? idempotencyKey, string? renderBundleId, Guid actor, CancellationToken ct)
    {
        if (kind is null || !PrescriptionIssueKinds.All.Contains(kind))
            throw new PrescriptionException("validation_failed", "Choose a print or a record copy.", 400);
        var key = idempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > 100)
            throw new PrescriptionException("validation_failed", "An idempotency key of 1 to 100 characters is required.", 400);

        var existing = await db.PrescriptionIssues.AsNoTracking().SingleOrDefaultAsync(i => i.PrescriptionId == prescriptionId && i.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            if (existing.IssueKind != kind) throw new PrescriptionException("idempotency_key_reused", "That key was already used for a different issue of this prescription.", 409);
            return new IssuedPrescription(existing, true);
        }

        var r = await records.GetAsync(prescriptionId, ct);
        RequireDetails(r);
        if (kind == PrescriptionIssueKinds.Print && r.Status != PrescriptionStatuses.Active)
            throw new PrescriptionException("prescription_not_active", $"This prescription is {r.Status.ToLowerInvariant()}, so it cannot be printed to dispense. Issue a record copy instead.", 409);

        var patient = await db.Patients.AsNoTracking().SingleAsync(p => p.Id == r.PatientId, ct);
        if (string.IsNullOrWhiteSpace(patient.AddressLine1) || string.IsNullOrWhiteSpace(patient.City) || string.IsNullOrWhiteSpace(patient.State) || string.IsNullOrWhiteSpace(patient.PostalCode))
            throw new PrescriptionException("print_incomplete", "The patient's address is missing, so the prescription cannot be printed. Add the address to the patient's details first.", 409);

        var prescriberUserId = (await db.Prescriptions.AsNoTracking().SingleAsync(p => p.Id == prescriptionId, ct)).PrescriberUserId;
        var staff = await db.StaffProfiles.AsNoTracking().Where(s => s.UserAccountId == prescriberUserId).OrderByDescending(s => s.IsActive).FirstOrDefaultAsync(ct);
        var practice = await db.PracticeSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var envelope = new PrescriptionIssueEnvelope(
            new IssuePatient(string.Join(' ', new[] { patient.FirstName, patient.MiddleName, patient.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))), patient.DateOfBirth.ToString("O", CultureInfo.InvariantCulture),
                patient.Mrn, patient.AddressLine1, Blank(patient.AddressLine2), patient.City, patient.State, patient.PostalCode, patient.Country),
            new IssuePrescriber(r.PrescribedByName ?? staff?.DisplayName ?? "", Blank(staff?.CredentialLabel), Blank(staff?.LicenseNumber), Blank(staff?.LicenseJurisdiction), Blank(staff?.Npi)),
            practice is null ? null : new IssuePractice(practice.Name, Blank(practice.Phone), Blank(practice.AddressLine)));

        var events = await EventsAsync(prescriptionId, ct);
        var ending = events.LastOrDefault(e => PrescriptionEventTypes.Ending.Contains(e.EventType));
        var issue = new PrescriptionIssue
        {
            Id = Guid.NewGuid(), PrescriptionId = prescriptionId, IssueKind = kind, IssuedAtUtc = clock.UtcNow, IssuedByUserId = actor, TemplateVersion = TemplateVersion, StatusAtIssue = r.Status,
            EventCutoffId = (ending ?? events.LastOrDefault())?.Id, EnvelopeJson = JsonSerializer.Serialize(envelope), RenderBundleId = renderBundleId, IdempotencyKey = key, CorrelationId = Guid.NewGuid(),
        };
        db.PrescriptionIssues.Add(issue);
        AuditService.Record(db, "PrescriptionIssued", nameof(PrescriptionIssue), issue.Id, actor, $"{kind} of a prescription issued (template {TemplateVersion}).", correlationId: issue.CorrelationId);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            // a concurrent retry with the same key won: return its issue; any other failure is not swallowed
            var winner = await db.PrescriptionIssues.AsNoTracking().SingleOrDefaultAsync(i => i.PrescriptionId == prescriptionId && i.IdempotencyKey == key, ct);
            if (winner is null)
            {
                logger.LogWarning("prescription_issue_not_saved error_class={ErrorClass} correlation_id={CorrelationId}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, issue.CorrelationId);
                throw new PrescriptionException("print_failed", "The prescription could not be issued. Nothing was printed. Try again.", 503);
            }
            if (winner.IssueKind != kind) throw new PrescriptionException("idempotency_key_reused", "That key was already used for a different issue of this prescription.", 409);
            return new IssuedPrescription(winner, true);
        }
        return new IssuedPrescription(issue, false);
    }

    /// <summary>The issue and the patient it belongs to, resolved through its prescription (never from the caller).</summary>
    public async Task<(PrescriptionIssue Issue, Guid PatientId)> GetAsync(Guid issueId, CancellationToken ct)
    {
        var found = await db.PrescriptionIssues.AsNoTracking().Where(i => i.Id == issueId)
            .Join(db.Prescriptions.AsNoTracking(), i => i.PrescriptionId, p => p.Id, (i, p) => new { Issue = i, p.PatientId }).SingleOrDefaultAsync(ct)
            ?? throw new PrescriptionException("prescription_issue_not_found", "That prescription issue was not found.", 404);
        return (found.Issue, found.PatientId);
    }

    /// <summary>The template input for an issue: the prescription exactly as recorded, the status recorded at issue (with its date and reason), and the envelope. Nothing from current patient, staff or practice data.</summary>
    public async Task<PrescriptionPdf.Request> PdfAsync(PrescriptionIssue issue, DateTimeOffset producedAtUtc, CancellationToken ct)
    {
        var r = await records.GetAsync(issue.PrescriptionId, ct);
        RequireDetails(r);
        var cutoff = issue.EventCutoffId is { } id ? await db.PrescriptionEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && e.PrescriptionId == issue.PrescriptionId, ct) : null;
        var ended = cutoff is not null && PrescriptionEventTypes.Ending.Contains(cutoff.EventType) ? cutoff : null;
        var envelope = JsonSerializer.Deserialize<PrescriptionIssueEnvelope>(issue.EnvelopeJson)
            ?? throw new PrescriptionException("prescription_issue_unreadable", "This prescription issue could not be read.", 409);
        return PrescriptionPdf.For(issue, r, envelope, ended?.OccurredAtUtc, ended?.Reason, clock, producedAtUtc);
    }

    /// <summary>The prescription's events in the order the record reader shows them.</summary>
    private async Task<List<PrescriptionEvent>> EventsAsync(Guid prescriptionId, CancellationToken ct) =>
        (await db.PrescriptionEvents.AsNoTracking().Where(e => e.PrescriptionId == prescriptionId).ToListAsync(ct))
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.EventType == PrescriptionEventTypes.Finalized ? 0 : 1).ToList();

    private static void RequireDetails(PrescriptionRecord r)
    {
        if (!r.HasDetails || r.Instructions is null || r.QuantityValue is null || r.QuantityUnit is null || r.Refills is null)
            throw new PrescriptionException("print_incomplete",
                "This prescription was saved before instructions, quantity and refills were recorded, so there is nothing complete to print. Amend it to add them.", 409);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
