namespace Alveara.Api.Architecture.Billing;

/// <summary>Why part of an allocation was released: a <see cref="Refund"/> returned money that had been applied to a charge, or a <see cref="Correction"/> moved an application to another charge.</summary>
public static class ReleaseCauses
{
    public const string Refund = "Refund";
    public const string Correction = "Correction";
    public static readonly IReadOnlyList<string> All = [Refund, Correction];
}

/// <summary>
/// ALV-007-C02: the historical negative of part of one allocation. Allocations are append-only and never edited, so undoing part of one is a release row beside it: what a charge has been paid is its allocations
/// minus their releases (as before, excluding reversed payments), and what a payment has applied is likewise net of releases. A release caused by a refund names that refund and releases an allocation of the
/// refunded payment; a release caused by a correction carries a reason. Appended, never edited or deleted; never more than its allocation still holds.
/// </summary>
public class BillingAllocationRelease
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>1, 2, 3... unique per account.</summary>
    public int ReleaseNumber { get; set; }
    public Guid AllocationId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>One of <see cref="ReleaseCauses.All"/>.</summary>
    public required string Cause { get; set; }
    /// <summary>Set exactly for a release caused by a refund.</summary>
    public Guid? RefundEntryId { get; set; }
    /// <summary>Required for a correction; the reason of a refund is the refund entry's.</summary>
    public string? Reason { get; set; }
    /// <summary>The caller's key for the save that made this release; unique per account (releases made by one operation share it).</summary>
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset ReleasedAtUtc { get; set; }
    public Guid ReleasedByUserId { get; set; }
}
