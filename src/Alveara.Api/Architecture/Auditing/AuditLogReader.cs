using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Auditing;

/// <summary>
/// One audit row as the audit page reads it. ALV-N025 adds:
/// - <c>PerformedByName</c> and, for account rows, <c>TargetName</c> (the actor-name rule; the ids stay, F-149);
/// - <c>HistoryEntryId</c> on a patient-edit row: an OPAQUE reference to the first History entry of that edit (F-041, BR-02). No old or new value, no patient name and
///   no contact detail is ever returned here, for any caller; resolving the reference goes only through <c>GET /api/patients/{id}/history</c>, which requires
///   <c>ViewPatientRecords</c> on its own.
/// </summary>
/// ALV-N030-C01 (brief 18.10, Q7): owner rows store ids only; <c>SubjectName</c> (the staff member the row is about), <c>FormerOwnerName</c> and <c>NewOwnerName</c>
/// are resolved here, at read time, for the ViewAuditLog holder - never written back into the row.
public sealed record AuditLogEntryView(Guid Id, string EventType, Guid TargetUserAccountId, Guid? PerformedByUserAccountId, string Details, DateTimeOffset TimestampUtc,
    string? EntityType, string? Reason, Guid? CorrelationId, string? PerformedByName, string? TargetName, Guid? HistoryEntryId,
    string? SubjectName = null, string? FormerOwnerName = null, string? NewOwnerName = null);

/// <summary>The filters of the audit page (F-150): an exact event type and a range of instants; <c>Take</c> is bounded to 1-500 as before.</summary>
public sealed record AuditLogQuery(int Take = 100, string? EventType = null, DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null);

/// <summary>
/// ALV-N025 (PKG-10 group A): reads the audit log for <c>ViewAuditLog</c> holders - most recent first, bounded, filtered - and names the people in it. The rows stay PHI-free:
/// names are resolved when read and are only the staff members' (or account) names, never a patient's.
/// </summary>
public sealed class AuditLogReader(AlveraDbContext db)
{
    public const string PatientUpdated = "PatientUpdated";

    public async Task<IReadOnlyList<AuditLogEntryView>> ReadAsync(AuditLogQuery query, CancellationToken ct)
    {
        var rows = db.AuditLogEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.EventType)) rows = rows.Where(a => a.EventType == query.EventType.Trim());
        if (query.FromUtc is { } from) rows = rows.Where(a => a.TimestampUtc >= from);
        if (query.ToUtc is { } to) rows = rows.Where(a => a.TimestampUtc <= to);
        var entries = await rows.OrderByDescending(a => a.TimestampUtc).Take(Math.Clamp(query.Take, 1, 500)).ToListAsync(ct);

        var accountTargets = entries.Where(IsAccountRow).Select(e => (Guid?)e.TargetUserAccountId);
        var names = await ClinicalNames.ResolveAsync(db, entries.Select(e => e.PerformedByUserAccountId).Concat(accountTargets), ct);
        var staffNames = await OwnerStaffNamesAsync(entries, ct);
        string? Staff(Guid? id) => id is { } g && staffNames.TryGetValue(g, out var n) ? n : null;
        return entries.Select(e =>
        {
            var owner = OwnerEvents.Contains(e.EventType);
            return new AuditLogEntryView(e.Id, e.EventType, e.TargetUserAccountId, e.PerformedByUserAccountId, e.Details, e.TimestampUtc, e.EntityType, e.Reason,
                e.CorrelationId, ClinicalNames.Name(names, e.PerformedByUserAccountId), IsAccountRow(e) ? ClinicalNames.Name(names, e.TargetUserAccountId) : null,
                e.EventType == PatientUpdated ? e.CorrelationId : null,
                owner ? Staff(e.TargetUserAccountId) : null,
                owner ? Staff(DetailId(e.Details, "formerOwnerStaffProfileId")) : null,
                owner ? Staff(DetailId(e.Details, "newOwnerStaffProfileId")) : null);
        }).ToList();
    }

    private static readonly HashSet<string> OwnerEvents = [Identity.AuditEventTypes.PracticeOwnerDesignated, Identity.AuditEventTypes.PracticeOwnerTransferred];

    private static Guid? DetailId(string details, string key) => Guid.TryParse(Identity.PracticeOwnerTransferService.Field(details, key), out var g) ? g : null;

    /// <summary>The display names of the staff members the owner rows name (subject, former and new owner), in one query.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> OwnerStaffNamesAsync(IReadOnlyList<Identity.AuditLogEntry> entries, CancellationToken ct)
    {
        var ids = entries.Where(e => OwnerEvents.Contains(e.EventType))
            .SelectMany(e => new[] { (Guid?)e.TargetUserAccountId, DetailId(e.Details, "formerOwnerStaffProfileId"), DetailId(e.Details, "newOwnerStaffProfileId") })
            .OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await db.StaffProfiles.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);
    }

    /// <summary>The event types present in the log, for the page's filter (at most 200).</summary>
    public async Task<IReadOnlyList<string>> EventTypesAsync(CancellationToken ct) =>
        await db.AuditLogEntries.AsNoTracking().Select(a => a.EventType).Distinct().OrderBy(t => t).Take(200).ToListAsync(ct);

    /// <summary>Rows about an account (sign-in, role, enablement...) name their target; rows about a patient, a document or anything else never do.</summary>
    private static bool IsAccountRow(Identity.AuditLogEntry e) => e.EntityType is null or "UserAccount";
}
