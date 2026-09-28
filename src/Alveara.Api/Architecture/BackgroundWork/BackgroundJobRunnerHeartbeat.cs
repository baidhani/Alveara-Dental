namespace Alveara.Api.Architecture.BackgroundWork;

/// <summary>
/// Tracks when the background job runner last completed a poll cycle, so the System Status
/// endpoint can report truthful runner health rather than assuming the hosted service is alive
/// merely because the process is running.
/// </summary>
public sealed class BackgroundJobRunnerHeartbeat
{
    private DateTimeOffset? _lastPollUtc;
    public DateTimeOffset? LastPollUtc => _lastPollUtc;
    public void RecordPoll() => _lastPollUtc = DateTimeOffset.UtcNow;
}
