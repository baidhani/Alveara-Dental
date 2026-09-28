namespace Alveara.Api.Architecture.BackgroundWork;

/// <summary>
/// Durable "this job's effect was already applied" receipt, per N002-R01-01. Written in the same
/// database transaction as the handler's own effect and the job's Succeeded status — so if the
/// process crashes after the effect commits but before the job could be marked Succeeded, the
/// receipt commits too (or neither does), and recovery can tell "effect already happened, just
/// finish bookkeeping" apart from "effect never happened, safe to run the handler."
/// </summary>
public class BackgroundJobEffectReceipt
{
    public Guid JobId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
