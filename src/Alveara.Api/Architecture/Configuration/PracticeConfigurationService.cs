using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Configuration;

public record PracticeSettingsView(
    Guid? Id, string? Name, string? Phone, string? AddressLine, string TimeZoneId, string Currency, bool Configured, string? RowVersion);

/// <summary>
/// ALV-N003: practice information, the (single active) location, operatories, and appointment
/// types. Every mutation is audited through the shared AuditService inside the same SaveChanges as
/// the change, carries optimistic concurrency, and never deletes: configuration that scheduling
/// may later reference is only ever inactivated, so history stays resolvable.
/// </summary>
public class PracticeConfigurationService(AlveraDbContext db, IPracticeClock clock)
{
    // ---------- Practice settings ----------

    public async Task<PracticeSettingsView> GetPracticeAsync(CancellationToken ct)
    {
        var settings = await db.PracticeSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        return ToView(settings);
    }

    /// <summary>Creates the practice's settings on first save, thereafter updates them (version required).</summary>
    public async Task<PracticeSettingsView> SavePracticeAsync(string? name, string? phone, string? addressLine, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var cleanName = ConfigurationWrite.RequireName(name, "Practice name");
        var cleanPhone = ConfigurationWrite.OptionalText(phone, "Phone", 40);
        var cleanAddress = ConfigurationWrite.OptionalText(addressLine, "Address", 200);

        var existing = await db.PracticeSettings.SingleOrDefaultAsync(ct);
        if (existing is null)
        {
            existing = new PracticeSettings { Name = cleanName, Phone = cleanPhone, AddressLine = cleanAddress, UpdatedAtUtc = clock.UtcNow };
            db.PracticeSettings.Add(existing);
            ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Created, nameof(PracticeSettings), existing.Id, actor, "Practice information created.");
        }
        else
        {
            ConfigurationWrite.ApplyExpectedVersion(db, existing, rowVersion);
            existing.Name = cleanName;
            existing.Phone = cleanPhone;
            existing.AddressLine = cleanAddress;
            existing.UpdatedAtUtc = clock.UtcNow;
            ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Updated, nameof(PracticeSettings), existing.Id, actor, "Practice information updated.");
        }

        await ConfigurationWrite.SaveAsync(db, nameof(PracticeSettings), existing.Id, ct);
        return ToView(existing);
    }

    private PracticeSettingsView ToView(PracticeSettings? s) => new(
        s?.Id, s?.Name, s?.Phone, s?.AddressLine,
        clock.PracticeTimeZone.Id, Money.Money.Currency, s is not null,
        s is null ? null : Convert.ToBase64String(s.RowVersion));

    // ---------- Location (one active location in first release) ----------

    public Task<List<PracticeLocation>> ListLocationsAsync(bool includeInactive, CancellationToken ct) =>
        db.PracticeLocations.AsNoTracking().Where(l => includeInactive || l.IsActive).OrderBy(l => l.Name).ToListAsync(ct);

    public async Task<PracticeLocation> GetLocationAsync(Guid id, CancellationToken ct) =>
        await db.PracticeLocations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, ct)
        ?? throw NotFound("Location");

    public async Task<PracticeLocation> CreateLocationAsync(string? name, Guid actor, CancellationToken ct)
    {
        var cleanName = ConfigurationWrite.RequireName(name, "Location name");
        if (await db.PracticeLocations.AnyAsync(l => l.Name == cleanName, ct))
            throw new ConfigurationException("location_name_taken", "A location with that name already exists.", 409);
        // First release: one active location. A second active one is refused rather than silently
        // creating state later stories would have to reconcile.
        if (await db.PracticeLocations.AnyAsync(l => l.IsActive, ct))
            throw new ConfigurationException("second_active_location_not_supported",
                "Only one active location is supported. Inactivate the current location first.", 409);

        var location = new PracticeLocation { Id = Guid.NewGuid(), Name = cleanName, IsActive = true, CreatedAtUtc = clock.UtcNow };
        db.PracticeLocations.Add(location);
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Created, nameof(PracticeLocation), location.Id, actor, "Location created.");
        await ConfigurationWrite.SaveAsync(db, nameof(PracticeLocation), location.Id, ct);
        return location;
    }

    public async Task<PracticeLocation> UpdateLocationAsync(Guid id, string? name, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var cleanName = ConfigurationWrite.RequireName(name, "Location name");
        var location = await db.PracticeLocations.SingleOrDefaultAsync(l => l.Id == id, ct) ?? throw NotFound("Location");
        if (await db.PracticeLocations.AnyAsync(l => l.Id != id && l.Name == cleanName, ct))
            throw new ConfigurationException("location_name_taken", "A location with that name already exists.", 409);
        ConfigurationWrite.ApplyExpectedVersion(db, location, rowVersion);
        location.Name = cleanName;
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Updated, nameof(PracticeLocation), id, actor, "Location renamed.");
        await ConfigurationWrite.SaveAsync(db, nameof(PracticeLocation), id, ct);
        return location;
    }

    public async Task<PracticeLocation> SetLocationActiveAsync(Guid id, bool active, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var location = await db.PracticeLocations.SingleOrDefaultAsync(l => l.Id == id, ct) ?? throw NotFound("Location");
        if (location.IsActive == active) return location; // idempotent: already in the requested state
        if (!active && await db.Operatories.AnyAsync(o => o.LocationId == id && o.IsActive, ct))
            throw new ConfigurationException("location_has_active_operatories",
                "Inactivate this location's operatories first.", 409);
        if (active && await db.PracticeLocations.AnyAsync(l => l.IsActive && l.Id != id, ct))
            throw new ConfigurationException("second_active_location_not_supported",
                "Only one active location is supported. Inactivate the current location first.", 409);

        ConfigurationWrite.ApplyExpectedVersion(db, location, rowVersion);
        location.IsActive = active;
        ConfigurationWrite.Audit(db, active ? ConfigurationAuditEvents.Reactivated : ConfigurationAuditEvents.Inactivated,
            nameof(PracticeLocation), id, actor, active ? "Location reactivated." : "Location inactivated.");
        await ConfigurationWrite.SaveAsync(db, nameof(PracticeLocation), id, ct);
        return location;
    }

    // ---------- Operatories ----------

    public Task<List<Operatory>> ListOperatoriesAsync(bool includeInactive, CancellationToken ct) =>
        db.Operatories.AsNoTracking().Where(o => includeInactive || o.IsActive).OrderBy(o => o.Name).ToListAsync(ct);

    public async Task<Operatory> GetOperatoryAsync(Guid id, CancellationToken ct) =>
        await db.Operatories.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw NotFound("Operatory");

    public async Task<Operatory> CreateOperatoryAsync(string? name, Guid actor, CancellationToken ct)
    {
        var cleanName = ConfigurationWrite.RequireName(name, "Operatory name");
        var location = await db.PracticeLocations.SingleOrDefaultAsync(l => l.IsActive, ct)
            ?? throw new ConfigurationException("no_active_location", "Create an active location before adding operatories.", 409);
        if (await db.Operatories.AnyAsync(o => o.LocationId == location.Id && o.Name == cleanName, ct))
            throw new ConfigurationException("operatory_name_taken", "An operatory with that name already exists at this location.", 409);

        var operatory = new Operatory { Id = Guid.NewGuid(), LocationId = location.Id, Name = cleanName, IsActive = true, CreatedAtUtc = clock.UtcNow };
        db.Operatories.Add(operatory);
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Created, nameof(Operatory), operatory.Id, actor, "Operatory created.");
        await ConfigurationWrite.SaveAsync(db, nameof(Operatory), operatory.Id, ct);
        return operatory;
    }

    public async Task<Operatory> UpdateOperatoryAsync(Guid id, string? name, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var cleanName = ConfigurationWrite.RequireName(name, "Operatory name");
        var operatory = await db.Operatories.SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw NotFound("Operatory");
        if (await db.Operatories.AnyAsync(o => o.Id != id && o.LocationId == operatory.LocationId && o.Name == cleanName, ct))
            throw new ConfigurationException("operatory_name_taken", "An operatory with that name already exists at this location.", 409);
        ConfigurationWrite.ApplyExpectedVersion(db, operatory, rowVersion);
        operatory.Name = cleanName;
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Updated, nameof(Operatory), id, actor, "Operatory renamed.");
        await ConfigurationWrite.SaveAsync(db, nameof(Operatory), id, ct);
        return operatory;
    }

    public async Task<Operatory> SetOperatoryActiveAsync(Guid id, bool active, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var operatory = await db.Operatories.Include(o => o.Location).SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw NotFound("Operatory");
        if (operatory.IsActive == active) return operatory;
        if (active && operatory.Location is { IsActive: false })
            throw new ConfigurationException("location_inactive", "This operatory's location is inactive. Reactivate the location first.", 409);
        ConfigurationWrite.ApplyExpectedVersion(db, operatory, rowVersion);
        operatory.IsActive = active;
        ConfigurationWrite.Audit(db, active ? ConfigurationAuditEvents.Reactivated : ConfigurationAuditEvents.Inactivated,
            nameof(Operatory), id, actor, active ? "Operatory reactivated." : "Operatory inactivated.");
        await ConfigurationWrite.SaveAsync(db, nameof(Operatory), id, ct);
        return operatory;
    }

    // ---------- Appointment types ----------

    public Task<List<AppointmentType>> ListAppointmentTypesAsync(bool includeInactive, CancellationToken ct) =>
        db.AppointmentTypes.AsNoTracking().Where(t => includeInactive || t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);

    public async Task<AppointmentType> GetAppointmentTypeAsync(Guid id, CancellationToken ct) =>
        await db.AppointmentTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw NotFound("Appointment type");

    public async Task<AppointmentType> CreateAppointmentTypeAsync(string? name, int durationMinutes, Guid actor, CancellationToken ct)
    {
        var cleanName = ConfigurationWrite.RequireName(name, "Appointment type name");
        ValidateDuration(durationMinutes);
        if (await db.AppointmentTypes.AnyAsync(t => t.Name == cleanName, ct))
            throw new ConfigurationException("appointment_type_name_taken", "An appointment type with that name already exists.", 409);

        var type = new AppointmentType { Id = Guid.NewGuid(), Name = cleanName, DefaultDurationMinutes = durationMinutes, IsActive = true, CreatedAtUtc = clock.UtcNow };
        db.AppointmentTypes.Add(type);
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Created, nameof(AppointmentType), type.Id, actor,
            $"Appointment type created with a default duration of {durationMinutes} minutes.");
        await ConfigurationWrite.SaveAsync(db, nameof(AppointmentType), type.Id, ct);
        return type;
    }

    public async Task<AppointmentType> UpdateAppointmentTypeAsync(Guid id, string? name, int durationMinutes, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var cleanName = ConfigurationWrite.RequireName(name, "Appointment type name");
        ValidateDuration(durationMinutes);
        var type = await db.AppointmentTypes.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw NotFound("Appointment type");
        if (await db.AppointmentTypes.AnyAsync(t => t.Id != id && t.Name == cleanName, ct))
            throw new ConfigurationException("appointment_type_name_taken", "An appointment type with that name already exists.", 409);
        ConfigurationWrite.ApplyExpectedVersion(db, type, rowVersion);
        var previous = type.DefaultDurationMinutes;
        type.Name = cleanName;
        type.DefaultDurationMinutes = durationMinutes;
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Updated, nameof(AppointmentType), id, actor,
            previous == durationMinutes ? "Appointment type updated." : $"Appointment type updated; default duration changed from {previous} to {durationMinutes} minutes.");
        await ConfigurationWrite.SaveAsync(db, nameof(AppointmentType), id, ct);
        return type;
    }

    public async Task<AppointmentType> SetAppointmentTypeActiveAsync(Guid id, bool active, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var type = await db.AppointmentTypes.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw NotFound("Appointment type");
        if (type.IsActive == active) return type;
        ConfigurationWrite.ApplyExpectedVersion(db, type, rowVersion);
        type.IsActive = active;
        ConfigurationWrite.Audit(db, active ? ConfigurationAuditEvents.Reactivated : ConfigurationAuditEvents.Inactivated,
            nameof(AppointmentType), id, actor, active ? "Appointment type reactivated." : "Appointment type inactivated.");
        await ConfigurationWrite.SaveAsync(db, nameof(AppointmentType), id, ct);
        return type;
    }

    public static void ValidateDuration(int minutes)
    {
        if (minutes < AppointmentType.MinDurationMinutes || minutes > AppointmentType.MaxDurationMinutes
            || minutes % AppointmentType.DurationStepMinutes != 0)
        {
            throw new ConfigurationException("invalid_duration",
                $"Duration must be between {AppointmentType.MinDurationMinutes} and {AppointmentType.MaxDurationMinutes} minutes, in steps of {AppointmentType.DurationStepMinutes}.");
        }
    }

    internal static ConfigurationException NotFound(string what) => new("not_found", $"{what} was not found.", 404);
}
