using Alveara.Api.Architecture.Time;

namespace Alveara.Api.ChainHost;

/// <summary>
/// The chain's injected test clock (PD-32). "Now" is a fixed instant that only <see cref="Advance"/> moves; every time-zone conversion is delegated to the real <see cref="PracticeClock"/> so the
/// practice's time model is unchanged. It replaces <see cref="IPracticeClock"/> and nothing else: authentication expiry, retry and back-off keep real time (a change there needs its own review).
/// </summary>
public sealed class ChainPracticeClock : IPracticeClock
{
    private readonly PracticeClock _zone;
    private long _ticks;

    public ChainPracticeClock(DateTimeOffset start, TimeZoneInfo practiceTimeZone)
    {
        _zone = new PracticeClock(practiceTimeZone);
        _ticks = start.UtcTicks;
    }

    public TimeZoneInfo PracticeTimeZone => _zone.PracticeTimeZone;

    public DateTimeOffset UtcNow => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    /// <summary>Moves the injected "now" forward. A negative step is refused: time in the chain only runs forward.</summary>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(by), "The chain clock cannot move backwards.");
        Interlocked.Add(ref _ticks, by.Ticks);
    }

    public DateTimeOffset ToPracticeLocal(DateTimeOffset utcInstant) => _zone.ToPracticeLocal(utcInstant);

    public DateTimeOffset FromPracticeLocal(DateTime localTime, LocalTimeAmbiguityPolicy ambiguityPolicy = LocalTimeAmbiguityPolicy.Reject) => _zone.FromPracticeLocal(localTime, ambiguityPolicy);
}
