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
    /// <summary>ALV-N021 A2 (5.2): when true (the default), a visit is checked in only after it was confirmed; when false, from Scheduled too.</summary>
    public bool ConfirmationRequired { get; set; } = true;
    /// <summary>ALV-N021 A2 (5.2): when true (the default), a patient is seated only after the visit was marked ready; when false, straight from checked in too.</summary>
    public bool MarkReadyRequired { get; set; } = true;
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
    /// <summary>
    /// ALV-N021 A2 (F-052, migration M5, owner decision 2026-11-15): how the block is shown on the calendar to everyone who can see the schedule - one of <see cref="BlockedTimeLabels"/>,
    /// never free text. The <see cref="Reason"/> stays private to practice configuration. Null on blocks added before M5 (shown through <see cref="BlockedTimeLabels.ForCalendar"/>).
    /// </summary>
    public string? CalendarLabel { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>ALV-N021 A2 (F-052, M5): the controlled calendar labels of blocked time, and the one rule that turns a block into what the calendar may show.</summary>
public static class BlockedTimeLabels
{
    public const string Lunch = "lunch";
    public const string Meeting = "meeting";
    public const string Training = "training";
    public const string Unavailable = "unavailable";
    public const string Other = "other";
    public static readonly IReadOnlyList<string> All = [Lunch, Meeting, Training, Unavailable, Other];

    /// <summary>What a block added before M5 (no label) shows: never its reason.</summary>
    public const string Unlabelled = "Blocked time";

    private static readonly Dictionary<string, string> Display = new(StringComparer.Ordinal)
    {
        [Lunch] = "Lunch", [Meeting] = "Meeting", [Training] = "Training", [Unavailable] = "Unavailable", [Other] = "Other blocked time",
    };

    /// <summary>The only legacy reasons recognised as lunch (trimmed, case ignored); every other legacy reason is shown as <see cref="Unlabelled"/>, never guessed and never shown.</summary>
    private static readonly string[] LegacyLunch = ["Lunch", "Staff lunch"];

    public static bool IsValid(string? code) => code is not null && Display.ContainsKey(code);

    /// <summary>The words the calendar shows for a block: its label, or for a pre-M5 block "Lunch" only when its reason is exactly a recognised lunch wording, otherwise "Blocked time".</summary>
    public static string ForCalendar(string? calendarLabel, string? legacyReason)
    {
        if (calendarLabel is not null && Display.TryGetValue(calendarLabel, out var shown)) return shown;
        var reason = legacyReason?.Trim();
        return reason is not null && LegacyLunch.Any(l => string.Equals(l, reason, StringComparison.OrdinalIgnoreCase)) ? Display[Lunch] : Unlabelled;
    }
}
