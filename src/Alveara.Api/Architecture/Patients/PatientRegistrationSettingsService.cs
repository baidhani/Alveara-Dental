using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients;

public record PatientRegistrationSettingsView(bool RequireEmail, bool RequireSex, string RowVersion, string? UpdatedAtUtc);

/// <summary>
/// ALV-003-C01: "validation appropriate to configurable practice requirements". A practice may require an email address and/or the
/// patient's sex in addition to the always-required fields. The server is the authority: registration, edit and the duplicate check
/// all load these settings and apply them, so a client that ignores them is still refused with the same per-field message.
/// Changing a requirement does not invalidate existing patients; it applies the next time a patient is registered or edited
/// (an edit of a patient who lacks a newly required field prompts for it). Saves carry the row version (a stale save is a 409),
/// and the change is audited with who and when.
/// </summary>
public class PatientRegistrationSettingsService(AlveraDbContext db, IPracticeClock clock)
{
    /// <summary>The practice's extra requirements (none if never configured). Used by the registration and edit services.</summary>
    public static async Task<PatientRequirements> LoadRequirementsAsync(AlveraDbContext db, CancellationToken ct)
    {
        var row = await db.PatientRegistrationSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        return row is null ? PatientRequirements.None : new PatientRequirements(row.RequireEmail, row.RequireSex);
    }

    public async Task<PatientRegistrationSettingsView> GetAsync(CancellationToken ct)
    {
        var row = await db.PatientRegistrationSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        return ToView(row);
    }

    /// <param name="rowVersion">The version the caller read; for a practice that has never saved settings, the empty string.</param>
    public async Task<PatientRegistrationSettingsView> UpdateAsync(bool requireEmail, bool requireSex, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var row = await db.PatientRegistrationSettings.SingleOrDefaultAsync(ct);
        var now = clock.UtcNow;

        if (row is null)
        {
            if (rowVersion is null) throw new PatientException("row_version_required", "The version you are editing is required so a concurrent change is never overwritten. Reload and try again.", 400);
            if (rowVersion != "") throw new Concurrency.ConcurrencyConflictException(nameof(PatientRegistrationSettings), Guid.Empty); // it was deleted or never existed under that version
            row = new PatientRegistrationSettings { Id = Guid.NewGuid(), RequireEmail = requireEmail, RequireSex = requireSex, UpdatedAtUtc = now, UpdatedByUserId = actor };
            db.PatientRegistrationSettings.Add(row);
            AuditService.Record(db, PatientAuditEventsV2.RequirementsChanged, nameof(PatientRegistrationSettings), row.Id, actor, $"Registration requirements set: email {(requireEmail ? "required" : "optional")}, sex {(requireSex ? "required" : "optional")}.");
        }
        else
        {
            // "" means the caller believed no settings existed yet; someone has saved since, so this is a stale save.
            if (rowVersion == "") throw new Concurrency.ConcurrencyConflictException(nameof(PatientRegistrationSettings), row.Id);
            PatientWrite.ApplyExpectedVersion(db, row, rowVersion);
            if (row.RequireEmail == requireEmail && row.RequireSex == requireSex) { db.ChangeTracker.Clear(); return ToView(await db.PatientRegistrationSettings.AsNoTracking().SingleAsync(ct)); }
            row.RequireEmail = requireEmail;
            row.RequireSex = requireSex;
            row.UpdatedAtUtc = now;
            row.UpdatedByUserId = actor;
            AuditService.Record(db, PatientAuditEventsV2.RequirementsChanged, nameof(PatientRegistrationSettings), row.Id, actor, $"Registration requirements set: email {(requireEmail ? "required" : "optional")}, sex {(requireSex ? "required" : "optional")}.");
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new Concurrency.ConcurrencyConflictException(nameof(PatientRegistrationSettings), row.Id);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            // Two first-ever saves raced: the unique Singleton index let only one create the row; the other must reload.
            throw new Concurrency.ConcurrencyConflictException(nameof(PatientRegistrationSettings), row.Id);
        }
        return ToView(row);
    }

    private static PatientRegistrationSettingsView ToView(PatientRegistrationSettings? row) =>
        row is null
            ? new PatientRegistrationSettingsView(false, false, "", null)
            : new PatientRegistrationSettingsView(row.RequireEmail, row.RequireSex, Convert.ToBase64String(row.RowVersion), row.UpdatedAtUtc?.ToString("O"));
}
