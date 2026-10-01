using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Architecture.Configuration;

/// <summary>
/// ALV-N003: the practice's own identity. A singleton row (<see cref="SingletonId"/>) - there is
/// exactly one practice per installation. Time zone and currency are deliberately NOT columns
/// here: they are deployment invariants owned by ALV-N002 (<c>PracticeTimeZone</c> in
/// configuration, consumed by <c>IPracticeClock</c>, and the fixed USD <c>Money.Currency</c>), and
/// the configuration API reflects those values read-only. Letting a database row disagree with the
/// clock that actually converts every stored local time would silently shift appointments, so
/// there is one source of truth, not two.
/// </summary>
public class PracticeSettings
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-00000000a1b3");

    public Guid Id { get; set; } = SingletonId;
    public required string Name { get; set; }
    public string? Phone { get; set; }
    public string? AddressLine { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// A physical practice location. The model is location-aware (operatories hang off a location) but
/// first release supports exactly one ACTIVE location - enforced by the service and backstopped by
/// a filtered unique index in AlveraDbContext. Multiple active locations / per-location overrides
/// are explicit future work.
/// </summary>
public class PracticeLocation
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>A chair/room appointments are scheduled into. Inactivated, never deleted, once in use.</summary>
public class Operatory
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public PracticeLocation? Location { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>A kind of appointment and how long it takes by default.</summary>
public class AppointmentType
{
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 480;
    public const int DurationStepMinutes = 5;

    public Guid Id { get; set; }
    public required string Name { get; set; }
    public int DefaultDurationMinutes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One recurring weekly working window for a provider, in PRACTICE-LOCAL wall-clock time
/// (<see cref="TimeOnly"/> + <see cref="DayOfWeek"/>), never a UTC instant. "Tuesdays 09:00-17:00"
/// means the same wall-clock hours before and after a daylight-saving change, which an
/// instant-based representation cannot express. Evaluated through <c>IPracticeClock</c>.
/// </summary>
public class ProviderWeeklyAvailability
{
    public Guid Id { get; set; }
    public Guid ProviderProfileId { get; set; }
    public ProviderProfile? ProviderProfile { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartLocal { get; set; }
    public TimeOnly EndLocal { get; set; }
}

/// <summary>
/// A specific period a provider is unavailable (vacation, meeting). Entered as practice-local
/// wall-clock time, converted once through <c>IPracticeClock.FromPracticeLocal</c> (which rejects a
/// DST-gap or ambiguous time rather than guessing), and stored as unambiguous UTC instants.
/// </summary>
public class ProviderBlockedTime
{
    public Guid Id { get; set; }
    public Guid ProviderProfileId { get; set; }
    public ProviderProfile? ProviderProfile { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
