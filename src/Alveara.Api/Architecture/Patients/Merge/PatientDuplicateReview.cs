using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Merge;

/// <summary>
/// ALV-017-C01: an administrator's decision that two patient records are NOT the same person, so the duplicate scan stops suggesting them. Never deleted and never edited: it can only be restored, which records who and when,
/// and a later dismissal of the same pair is a new row, so the history of decisions stays whole. One active dismissal per pair (<see cref="PairKey"/> is the pair in a fixed order, made by the database).
/// </summary>
public class PatientDuplicateDismissal
{
    public Guid Id { get; set; }
    public Guid FirstPatientId { get; set; }
    public Guid SecondPatientId { get; set; }
    /// <summary>Computed by the database: the two ids in one fixed order, so (A, B) and (B, A) are the same pair.</summary>
    public string PairKey { get; set; } = "";
    public required string Reason { get; set; }
    public Guid DismissedByUserId { get; set; }
    public DateTimeOffset DismissedAtUtc { get; set; }
    public Guid? RestoredByUserId { get; set; }
    public DateTimeOffset? RestoredAtUtc { get; set; }
}

public sealed record DismissRequest(Guid? FirstPatientId, Guid? SecondPatientId, string? Reason);

public sealed record DismissalView(Guid Id, PatientBrief First, PatientBrief Second, string Reason, Guid DismissedByUserId, string DismissedByName, DateTimeOffset DismissedAtUtc, bool Replayed);

/// <summary>
/// ALV-017-C01: dismissing a suggested pair as "not a duplicate" and restoring it. Each change is saved together with its audit entries (one for each record, with the user and the time), so a failing audit store changes
/// nothing. Dismissing a pair that is already dismissed answers with the dismissal already made; restoring one already restored does the same. A record that was merged away cannot be dismissed (<c>already_merged</c>).
/// Failure modes handled: a missing or unknown record, the same record twice, a retired record, a blank reason, a simultaneous duplicate dismissal (the unique rule answers, and the winner is returned). Who may do this is
/// decided by the permission on the controller.
/// </summary>
public class PatientDuplicateReviewService(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<DismissalView> DismissAsync(Guid actor, DismissRequest r, CancellationToken ct)
    {
        var first = r.FirstPatientId ?? throw new PatientException("first_required", "Choose the first record of the pair.", 400);
        var second = r.SecondPatientId ?? throw new PatientException("second_required", "Choose the second record of the pair.", 400);
        var reason = PatientMergeService.CleanReason(r.Reason);
        if (first == second) throw new PatientException("same_patient", "A record cannot be a duplicate of itself.", 400);

        var patients = await db.Patients.AsNoTracking().Where(p => p.Id == first || p.Id == second).ToListAsync(ct);
        if (patients.Count != 2) throw new PatientException("patient_not_found", "One of the records was not found.", 404);
        if (patients.Any(p => p.MergedAtUtc is not null)) throw new PatientException("already_merged", "One of the records was merged into another record and is no longer reviewed.", 409);

        if (await ActiveAsync(first, second, ct) is { } existing) return await ViewAsync(existing, true, ct);

        var row = new PatientDuplicateDismissal { Id = Guid.NewGuid(), FirstPatientId = first, SecondPatientId = second, Reason = reason, DismissedByUserId = actor, DismissedAtUtc = clock.UtcNow };
        db.PatientDuplicateDismissals.Add(row);
        Audit(AuditEventTypes.PatientDuplicateDismissed, "The pair of records was dismissed as not a duplicate.", row, reason, actor);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();                                                                                     // another request dismissed the same pair at the same moment: answer with theirs
            return await ViewAsync(await ActiveAsync(first, second, ct) ?? throw new PatientException("dismissal_conflict", "The pair was changed at the same moment. Reload.", 409), true, ct);
        }
        return await ViewAsync(row, false, ct);
    }

    public async Task<DismissalView> RestoreAsync(Guid actor, Guid id, CancellationToken ct)
    {
        var row = await db.PatientDuplicateDismissals.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new PatientException("dismissal_not_found", "That dismissal was not found.", 404);
        if (row.RestoredAtUtc is not null) return await ViewAsync(row, true, ct);
        row.RestoredByUserId = actor; row.RestoredAtUtc = clock.UtcNow;
        Audit(AuditEventTypes.PatientDuplicateRestored, "The pair of records is suggested as a possible duplicate again.", row, null, actor);
        await db.SaveChangesAsync(ct);
        return await ViewAsync(row, false, ct);
    }

    /// <summary>The dismissals in force, newest first.</summary>
    public async Task<IReadOnlyList<DismissalView>> ListAsync(CancellationToken ct)
    {
        var rows = await db.PatientDuplicateDismissals.AsNoTracking().Where(d => d.RestoredAtUtc == null).OrderByDescending(d => d.DismissedAtUtc).ThenBy(d => d.Id).Take(200).ToListAsync(ct);
        var result = new List<DismissalView>();
        foreach (var row in rows) result.Add(await ViewAsync(row, false, ct));
        return result;
    }

    /// <summary>The pairs in force as (smaller id, larger id), for the scan to leave out.</summary>
    public static async Task<HashSet<(Guid, Guid)>> ActivePairsAsync(AlveraDbContext db, CancellationToken ct)
    {
        var rows = await db.PatientDuplicateDismissals.AsNoTracking().Where(d => d.RestoredAtUtc == null).Select(d => new { d.FirstPatientId, d.SecondPatientId }).ToListAsync(ct);
        return rows.Select(r => Pair(r.FirstPatientId, r.SecondPatientId)).ToHashSet();
    }

    public static (Guid, Guid) Pair(Guid a, Guid b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);

    private Task<PatientDuplicateDismissal?> ActiveAsync(Guid a, Guid b, CancellationToken ct) =>
        db.PatientDuplicateDismissals.AsNoTracking().FirstOrDefaultAsync(d => d.RestoredAtUtc == null && ((d.FirstPatientId == a && d.SecondPatientId == b) || (d.FirstPatientId == b && d.SecondPatientId == a)), ct);

    private void Audit(string eventType, string text, PatientDuplicateDismissal row, string? reason, Guid actor)
    {
        AuditService.Record(db, eventType, nameof(Patient), row.FirstPatientId, actor, text + " This record is the first of the pair.", reason, row.Id);
        AuditService.Record(db, eventType, nameof(Patient), row.SecondPatientId, actor, text + " This record is the second of the pair.", reason, row.Id);
    }

    private async Task<DismissalView> ViewAsync(PatientDuplicateDismissal row, bool replayed, CancellationToken ct)
    {
        var patients = await db.Patients.AsNoTracking().Where(p => p.Id == row.FirstPatientId || p.Id == row.SecondPatientId).ToDictionaryAsync(p => p.Id, ct);
        var name = await db.UserAccounts.AsNoTracking().Where(u => u.Id == row.DismissedByUserId).Select(u => u.Username).FirstOrDefaultAsync(ct) ?? "";
        return new DismissalView(row.Id, PatientMergeService.BriefOf(patients[row.FirstPatientId]), PatientMergeService.BriefOf(patients[row.SecondPatientId]), row.Reason, row.DismissedByUserId, name, row.DismissedAtUtc, replayed);
    }
}
