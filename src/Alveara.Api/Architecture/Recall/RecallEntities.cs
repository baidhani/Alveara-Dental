namespace Alveara.Api.Architecture.Recall;

/// <summary>
/// ALV-009-C01: the practice's recall settings, owned by the recall domain (one row, never deleted). <see cref="Enabled"/> is the switch for automatic recall tasks and is off until the practice turns it on, so
/// deploying the story never fills the queue with a backlog; <see cref="DefaultIntervalDays"/>, <see cref="LeadDays"/> and <see cref="MaxVisitAgeMonths"/> are the numbers of <see cref="RecallRules"/>;
/// <see cref="DueSoonDays"/> is how far ahead the queue's "due soon" filter looks. <see cref="RowVersion"/> refuses a stale edit.
/// </summary>
public class RecallSettings
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public int DefaultIntervalDays { get; set; }
    public int LeadDays { get; set; }
    public int MaxVisitAgeMonths { get; set; }
    public int DueSoonDays { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// ALV-009-C01: one patient's own recall arrangement: their own interval in place of the practice default, and/or not being recalled at all. A patient with no row follows the default. The row means something
/// (an interval, an opt-out or both) or it would not exist; resetting a patient to the default removes it.
/// </summary>
public class PatientRecallPlan
{
    public Guid PatientId { get; set; }
    public int? IntervalDays { get; set; }
    public bool OptedOut { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
