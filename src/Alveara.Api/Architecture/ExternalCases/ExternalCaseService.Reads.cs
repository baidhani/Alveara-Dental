using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.ExternalCases;

public sealed record ExternalCaseQuery(string? Kind, string? Status, bool? Overdue, Guid? PatientId, Guid? PartyId, int? Page, int? PageSize);

public sealed record ExternalCaseListItem(ExternalCaseView Case, string PatientName);

/// <summary>How many cases the filters (other than status and overdue) would show: <see cref="Open"/> not finished, <see cref="Overdue"/> gone out and past their due date, <see cref="Closed"/> completed or cancelled.</summary>
public sealed record ExternalCaseCounts(int Open, int Overdue, int Closed);

public sealed record ExternalCasePage(IReadOnlyList<ExternalCaseListItem> Items, int Total, int Page, int PageSize, ExternalCaseCounts Counts);

/// <summary>One entry of a patient's referral and lab history across all their cases, newest first.</summary>
public sealed record PatientCaseTimelineEntry(string CaseKind, Guid CaseId, string CaseSummary, string CaseStatus, string EventKind, string? FromStatus, string? ToStatus, Guid? DocumentId, Guid ActorUserId, string? ActorName, DateTimeOffset OccurredAtUtc, string? Note);

public partial class ExternalCaseService
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 25;
    public const int MaxTimelineEntries = 200;

    private sealed class CaseRow
    {
        public string Kind { get; set; } = "";
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public DateOnly? DueOn { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
    }

    /// <summary>
    /// Referral and lab cases together, filtered and paged in the database: kind, status (<c>open</c> by default, <c>closed</c>, <c>all</c> or one status), overdue only, one patient, one outside party. Cases with a due date come
    /// first, soonest due first, then the rest newest first. The counts ignore the status and overdue filters so a screen can show how much each view holds.
    /// </summary>
    public async Task<ExternalCasePage> ListAsync(ExternalCaseQuery q, CancellationToken ct)
    {
        if (q.Kind is not null && !ExternalCaseKinds.All.Contains(q.Kind)) throw new ExternalCaseException("kind_invalid", "The kind is Referral or Lab.", 400);
        var status = string.IsNullOrWhiteSpace(q.Status) ? "open" : q.Status;
        var known = ExternalCaseRules.LabStatuses;
        if (status is not ("open" or "closed" or "all") && !known.Contains(status)) throw new ExternalCaseException("status_invalid", "The status is open, closed, all or one of the case statuses.", 400);
        var pageSize = Math.Clamp(q.PageSize ?? DefaultPageSize, 1, MaxPageSize);
        var page = Math.Max(1, q.Page ?? 1);
        var today = Today;
        var tracked = new[] { ExternalCaseRules.Sent, ExternalCaseRules.InProgress, ExternalCaseRules.Received };
        var finals = new[] { ExternalCaseRules.Completed, ExternalCaseRules.Cancelled };

        IQueryable<CaseRow> rows = Enumerable.Empty<CaseRow>().AsQueryable();
        var referrals = db.ReferralCases.AsNoTracking().Where(c => (q.PatientId == null || c.PatientId == q.PatientId) && (q.PartyId == null || c.PartyId == q.PartyId))
            .Select(c => new CaseRow { Kind = ExternalCaseKinds.Referral, Id = c.Id, Status = c.Status, DueOn = c.DueOn, CreatedAtUtc = c.CreatedAtUtc });
        var labs = db.LabCases.AsNoTracking().Where(c => (q.PatientId == null || c.PatientId == q.PatientId) && (q.PartyId == null || c.PartyId == q.PartyId))
            .Select(c => new CaseRow { Kind = ExternalCaseKinds.Lab, Id = c.Id, Status = c.Status, DueOn = c.DueOn, CreatedAtUtc = c.CreatedAtUtc });
        rows = q.Kind switch { ExternalCaseKinds.Referral => referrals, ExternalCaseKinds.Lab => labs, _ => referrals.Concat(labs) };

        var counts = new ExternalCaseCounts(
            await rows.CountAsync(r => !finals.Contains(r.Status), ct),
            await rows.CountAsync(r => tracked.Contains(r.Status) && r.DueOn != null && r.DueOn < today, ct),
            await rows.CountAsync(r => finals.Contains(r.Status), ct));

        var filtered = status switch
        {
            "open" => rows.Where(r => !finals.Contains(r.Status)),
            "closed" => rows.Where(r => finals.Contains(r.Status)),
            "all" => rows,
            _ => rows.Where(r => r.Status == status),
        };
        if (q.Overdue == true) filtered = filtered.Where(r => tracked.Contains(r.Status) && r.DueOn != null && r.DueOn < today);

        var total = await filtered.CountAsync(ct);
        var pageRows = await filtered.OrderBy(r => r.DueOn == null ? 1 : 0).ThenBy(r => r.DueOn).ThenByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var referralIds = pageRows.Where(r => r.Kind == ExternalCaseKinds.Referral).Select(r => r.Id).ToList();
        var labIds = pageRows.Where(r => r.Kind == ExternalCaseKinds.Lab).Select(r => r.Id).ToList();
        var entities = new Dictionary<Guid, IExternalCase>();
        foreach (var c in await db.ReferralCases.AsNoTracking().Where(c => referralIds.Contains(c.Id)).ToListAsync(ct)) entities[c.Id] = c;
        foreach (var c in await db.LabCases.AsNoTracking().Where(c => labIds.Contains(c.Id)).ToListAsync(ct)) entities[c.Id] = c;
        var patientIds = entities.Values.Select(e => e.PatientId).Distinct().ToList();
        var names = await db.Patients.AsNoTracking().Where(p => patientIds.Contains(p.Id)).Select(p => new { p.Id, p.FirstName, p.LastName }).ToDictionaryAsync(p => p.Id, p => $"{p.FirstName} {p.LastName}", ct);
        var items = new List<ExternalCaseListItem>();
        foreach (var r in pageRows)
        {
            var e = entities[r.Id];
            items.Add(new ExternalCaseListItem(await ViewAsync(e, ct), names.GetValueOrDefault(e.PatientId, "")));
        }
        return new ExternalCasePage(items, total, page, pageSize, counts);
    }

    /// <summary>Everything that happened to a patient's referrals and lab cases, newest first, at most <see cref="MaxTimelineEntries"/>, with who did it and what the case was.</summary>
    public async Task<IReadOnlyList<PatientCaseTimelineEntry>> PatientTimelineAsync(Guid patientId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var referrals = await db.ReferralCases.AsNoTracking().Where(c => c.PatientId == patientId).Select(c => new { c.Id, Summary = c.Reason, c.Status }).ToListAsync(ct);
        var labs = await db.LabCases.AsNoTracking().Where(c => c.PatientId == patientId).Select(c => new { c.Id, Summary = c.Title, c.Status }).ToListAsync(ct);
        var referralIds = referrals.Select(r => r.Id).ToList();
        var labIds = labs.Select(l => l.Id).ToList();
        var events = await (from e in db.ExternalCaseEvents.AsNoTracking()
                            where (e.ReferralCaseId != null && referralIds.Contains(e.ReferralCaseId.Value)) || (e.LabCaseId != null && labIds.Contains(e.LabCaseId.Value))
                            join u in db.UserAccounts.AsNoTracking() on e.ActorUserId equals u.Id into users
                            from u in users.DefaultIfEmpty()
                            orderby e.OccurredAtUtc descending, e.Id
                            select new { e.ReferralCaseId, e.LabCaseId, e.Kind, e.FromStatus, e.ToStatus, e.DocumentId, e.ActorUserId, ActorName = u == null ? null : u.Username, e.OccurredAtUtc, e.Note })
            .Take(MaxTimelineEntries).ToListAsync(ct);
        return events.Select(e => e.ReferralCaseId is { } r
            ? new PatientCaseTimelineEntry(ExternalCaseKinds.Referral, r, referrals.First(x => x.Id == r).Summary, referrals.First(x => x.Id == r).Status, e.Kind, e.FromStatus, e.ToStatus, e.DocumentId, e.ActorUserId, e.ActorName, e.OccurredAtUtc, e.Note)
            : new PatientCaseTimelineEntry(ExternalCaseKinds.Lab, e.LabCaseId!.Value, labs.First(x => x.Id == e.LabCaseId).Summary, labs.First(x => x.Id == e.LabCaseId).Status, e.Kind, e.FromStatus, e.ToStatus, e.DocumentId, e.ActorUserId, e.ActorName, e.OccurredAtUtc, e.Note)).ToList();
    }
}
