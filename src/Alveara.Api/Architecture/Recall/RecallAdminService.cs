using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Recall;

/// <summary>A refusal with a stable code: <c>settings_invalid</c>, <c>interval_invalid</c>, <c>plan_empty</c>, <c>reason_invalid</c>, <c>patient_not_found</c>, <c>row_version_required</c> (400/404); <c>settings_changed</c>, <c>plan_changed</c> (409).</summary>
public sealed class RecallException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record RecallSettingsView(bool Enabled, int DefaultIntervalDays, int LeadDays, int MaxVisitAgeMonths, int DueSoonDays, DateTimeOffset UpdatedAtUtc, Guid? UpdatedByUserId, string RowVersion);
/// <summary>The settings are replaced as a whole and carry the version the screen was showing.</summary>
public sealed record UpdateRecallSettingsRequest(bool? Enabled, int? DefaultIntervalDays, int? LeadDays, int? MaxVisitAgeMonths, int? DueSoonDays, string? RowVersion);

/// <param name="Verdict">The <see cref="RecallVerdict"/> for this patient now (why they are or are not due for a recall).</param>
public sealed record PatientRecallView(Guid PatientId, bool HasPlan, int? OwnIntervalDays, bool OptedOut, string? Reason, int EffectiveIntervalDays, DateTimeOffset? LastCompletedVisitUtc, DateTimeOffset? NextDueUtc, string Verdict, string? RowVersion);
public sealed record SetPatientRecallRequest(int? IntervalDays, bool OptedOut, string? Reason, string? RowVersion);
public sealed record ResetPatientRecallRequest(string? RowVersion);

/// <summary>
/// ALV-009-C01: the practice's recall settings and each patient's own recall arrangement. The settings are one row replaced as a whole with the version the screen showed (stale: <c>settings_changed</c> 409); every number is held
/// to the same limits as <see cref="RecallRules"/>; every change is audited with the old and new numbers (no patient data). A patient's plan is an interval of their own and/or an opt-out; setting the same thing again is a quiet
/// replay, a plan that exists needs its version to be changed or reset, and resetting a patient with no plan is a quiet replay. Who may call this is the controller's business.
/// </summary>
public class RecallAdminService(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<RecallSettingsView> GetSettingsAsync(CancellationToken ct) => ToView(await db.RecallSettings.AsNoTracking().SingleAsync(ct));

    public async Task<RecallSettingsView> UpdateSettingsAsync(Guid actor, UpdateRecallSettingsRequest r, CancellationToken ct)
    {
        var enabled = r.Enabled ?? throw new RecallException("settings_invalid", "Say whether automatic recall is on.", 400);
        var interval = Within(r.DefaultIntervalDays, RecallRules.MinIntervalDays, RecallRules.MaxIntervalDays, "The default recall interval is 1 to 3,650 days.");
        var lead = Within(r.LeadDays, 0, RecallRules.MaxLeadDays, "The lead time is 0 to 365 days.");
        var months = Within(r.MaxVisitAgeMonths, 1, RecallRules.MaxVisitAgeMonthsLimit, "The visit age limit is 1 to 120 months.");
        var dueSoon = Within(r.DueSoonDays, 1, 30, "The due-soon window is 1 to 30 days.");
        var version = Version(r.RowVersion);

        var s = await db.RecallSettings.SingleAsync(ct);
        if (s.Enabled == enabled && s.DefaultIntervalDays == interval && s.LeadDays == lead && s.MaxVisitAgeMonths == months && s.DueSoonDays == dueSoon) return ToView(s);   // nothing to change: a quiet replay
        db.Entry(s).Property(x => x.RowVersion).OriginalValue = version;
        var before = $"enabled {s.Enabled}, interval {s.DefaultIntervalDays}, lead {s.LeadDays}, visit age {s.MaxVisitAgeMonths}, due soon {s.DueSoonDays}";
        s.Enabled = enabled; s.DefaultIntervalDays = interval; s.LeadDays = lead; s.MaxVisitAgeMonths = months; s.DueSoonDays = dueSoon; s.UpdatedAtUtc = clock.UtcNow; s.UpdatedByUserId = actor;
        AuditService.Record(db, AuditEventTypes.RecallSettingsChanged, "RecallSettings", Guid.Empty, actor, $"Recall settings changed from ({before}) to (enabled {enabled}, interval {interval}, lead {lead}, visit age {months}, due soon {dueSoon}).");
        try { await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, "RecallSettings", Guid.Empty, ct); }
        catch (ConcurrencyConflictException) { throw new RecallException("settings_changed", "Someone else changed the recall settings. Reload them and try again.", 409); }
        return ToView(s);
    }

    public async Task<PatientRecallView> GetPatientAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new RecallException("patient_not_found", "That patient was not found.", 404);
        var settings = await db.RecallSettings.AsNoTracking().SingleAsync(ct);
        var plan = await db.PatientRecallPlans.AsNoTracking().FirstOrDefaultAsync(p => p.PatientId == patientId, ct);
        var now = clock.UtcNow;
        var last = await db.Appointments.AsNoTracking().Where(a => a.PatientId == patientId && a.Status == AppointmentStatuses.Scheduled && a.FlowState == PatientFlowStates.Completed && a.StartUtc <= now).MaxAsync(a => (DateTimeOffset?)a.StartUtc, ct);
        var open = await db.FollowUpTasks.AsNoTracking().AnyAsync(t => t.PatientId == patientId && t.Kind == FollowUpKinds.Recall && t.Status == FollowUpStatuses.Open, ct);
        var booked = await db.Appointments.AsNoTracking().AnyAsync(a => a.PatientId == patientId && a.Status == AppointmentStatuses.Scheduled && a.StartUtc > now, ct);
        var decision = RecallCalculator.Evaluate(new RecallInput(last, settings.DefaultIntervalDays, plan?.IntervalDays, plan?.OptedOut ?? false, settings.LeadDays, settings.MaxVisitAgeMonths, open, booked), now, clock);
        return new PatientRecallView(patientId, plan is not null, plan?.IntervalDays, plan?.OptedOut ?? false, plan?.Reason, decision.IntervalDays ?? settings.DefaultIntervalDays, last, decision.NextDueUtc, decision.Verdict.ToString(),
            plan is null ? null : Convert.ToBase64String(plan.RowVersion));
    }

    public async Task<PatientRecallView> SetPatientAsync(Guid actor, Guid patientId, SetPatientRecallRequest r, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new RecallException("patient_not_found", "That patient was not found.", 404);
        if (r.IntervalDays is { } days && !RecallRules.IntervalIsValid(days)) throw new RecallException("interval_invalid", "A recall interval is 1 to 3,650 days.", 400);
        if (r.IntervalDays is null && !r.OptedOut) throw new RecallException("plan_empty", "Choose an interval, or opt the patient out, or reset the patient to the practice default.", 400);
        var reason = r.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) reason = null;
        if (reason is not null && (reason.Length > 200 || reason.Any(char.IsControl))) throw new RecallException("reason_invalid", "The reason is up to 200 characters of plain text.", 400);

        var plan = await db.PatientRecallPlans.FirstOrDefaultAsync(p => p.PatientId == patientId, ct);
        var now = clock.UtcNow;
        if (plan is null)
        {
            db.PatientRecallPlans.Add(new PatientRecallPlan { PatientId = patientId, IntervalDays = r.IntervalDays, OptedOut = r.OptedOut, Reason = reason, UpdatedAtUtc = now, UpdatedByUserId = actor });
        }
        else
        {
            if (plan.IntervalDays == r.IntervalDays && plan.OptedOut == r.OptedOut && plan.Reason == reason) return await GetPatientAsync(patientId, ct);   // nothing to change: a quiet replay
            db.Entry(plan).Property(x => x.RowVersion).OriginalValue = Version(r.RowVersion);
            plan.IntervalDays = r.IntervalDays; plan.OptedOut = r.OptedOut; plan.Reason = reason; plan.UpdatedAtUtc = now; plan.UpdatedByUserId = actor;
        }
        AuditService.Record(db, AuditEventTypes.RecallPlanChanged, "PatientRecallPlan", patientId, actor, $"Recall plan set for patient {patientId}: interval {(r.IntervalDays?.ToString() ?? "default")}, opted out {r.OptedOut}.");
        await SaveAsync(patientId, ct);
        return await GetPatientAsync(patientId, ct);
    }

    public async Task<PatientRecallView> ResetPatientAsync(Guid actor, Guid patientId, ResetPatientRecallRequest r, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new RecallException("patient_not_found", "That patient was not found.", 404);
        var plan = await db.PatientRecallPlans.FirstOrDefaultAsync(p => p.PatientId == patientId, ct);
        if (plan is null) return await GetPatientAsync(patientId, ct);                                          // already on the default: a quiet replay
        db.Entry(plan).Property(x => x.RowVersion).OriginalValue = Version(r.RowVersion);
        db.PatientRecallPlans.Remove(plan);
        AuditService.Record(db, AuditEventTypes.RecallPlanReset, "PatientRecallPlan", patientId, actor, $"Recall plan reset to the practice default for patient {patientId}.");
        await SaveAsync(patientId, ct);
        return await GetPatientAsync(patientId, ct);
    }

    // ---------- helpers ----------

    private static RecallSettingsView ToView(RecallSettings s) => new(s.Enabled, s.DefaultIntervalDays, s.LeadDays, s.MaxVisitAgeMonths, s.DueSoonDays, s.UpdatedAtUtc, s.UpdatedByUserId, Convert.ToBase64String(s.RowVersion));

    private static int Within(int? value, int min, int max, string message) =>
        value is { } v && v >= min && v <= max ? v : throw new RecallException("settings_invalid", message, 400);

    private static byte[] Version(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) throw new RecallException("row_version_required", "Send the version you were shown so a change by someone else is never overwritten. Reload and try again.", 400);
        try { return Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new RecallException("row_version_required", "The version is not valid. Reload and try again.", 400); }
    }

    private async Task SaveAsync(Guid patientId, CancellationToken ct)
    {
        try { await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, "PatientRecallPlan", patientId, ct); }
        catch (ConcurrencyConflictException) { throw new RecallException("plan_changed", "Someone else changed this patient's recall plan. Reload it and try again.", 409); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 }) { throw new RecallException("plan_changed", "Someone else set this patient's recall plan. Reload it and try again.", 409); }
    }
}
