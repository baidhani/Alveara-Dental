namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// ALV-N021 A2 (OD-22 to OD-31, migration M3): one recorded checkout outcome of a visit. Append-only: a correction is a NEW row whose <see cref="SupersedesId"/> names the row it replaces,
/// never an edit (OD-31); the database refuses any update or delete. The current outcome of an item is its latest row that no other row supersedes.
///
/// Only decisions people make are stored here. "Treatment ended" is derived from the provider's End-treatment event and "follow-up booked" from a real future appointment (OD-24); neither
/// can be recorded by hand. Nothing here ever creates a payment, charge, claim, waiver or ledger row: a verified financial outcome only points at a ledger entry that already exists (OD-26).
/// </summary>
public class VisitCheckoutOutcome
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    /// <summary>One of <see cref="CheckoutItems"/>.</summary>
    public required string ItemCode { get; set; }
    /// <summary>One of <see cref="CheckoutOutcomes"/>, valid for the item.</summary>
    public required string OutcomeCode { get; set; }
    /// <summary>Required for <see cref="CheckoutOutcomes.Deferred"/> and <see cref="CheckoutOutcomes.WaiverRequested"/>; optional otherwise.</summary>
    public string? Reason { get; set; }
    /// <summary>The account's ledger entry a verified financial outcome relies on (a payment for <see cref="CheckoutOutcomes.PaymentRecorded"/>, a billing adjustment for <see cref="CheckoutOutcomes.Waived"/>).</summary>
    public int? LinkedLedgerEntryNumber { get; set; }
    /// <summary>A follow-up appointment the outcome refers to, when one exists (follow-up item only).</summary>
    public Guid? LinkedAppointmentId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    /// <summary>The earlier outcome of the same appointment and item that this one corrects; a row can be superseded once.</summary>
    public Guid? SupersedesId { get; set; }
    /// <summary>The caller's Idempotency-Key, so a retried request records nothing new.</summary>
    public required string IdempotencyKey { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>The checkout items that are recorded as decisions. Treatment ended is derived and has no code here.</summary>
public static class CheckoutItems
{
    public const string Departure = "departure";
    public const string FollowUp = "follow_up";
    public const string Financial = "financial";
    public static readonly IReadOnlyList<string> All = [Departure, FollowUp, Financial];
}

/// <summary>The outcomes a person can record, per item (OD-24, OD-26). Booked follow-up is derived, so it is not here.</summary>
public static class CheckoutOutcomes
{
    public const string PatientLeft = "patient_left";

    public const string NotClinicallyRequired = "not_clinically_required";
    public const string PatientDeclined = "patient_declined";
    public const string Deferred = "deferred";

    public const string PaymentRecorded = "payment_recorded";
    public const string Waived = "waived";
    public const string PaymentPlan = "payment_plan";
    public const string InsuranceToBill = "insurance_to_bill";
    public const string SendStatement = "send_statement";
    /// <summary>Recorded and shown, but it does NOT resolve the financial item (OD-26).</summary>
    public const string WaiverRequested = "waiver_requested";

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ByItem = new Dictionary<string, IReadOnlyList<string>>
    {
        [CheckoutItems.Departure] = [PatientLeft],
        [CheckoutItems.FollowUp] = [NotClinicallyRequired, PatientDeclined, Deferred],
        [CheckoutItems.Financial] = [PaymentRecorded, Waived, PaymentPlan, InsuranceToBill, SendStatement, WaiverRequested],
    };

    public static readonly IReadOnlySet<string> NeedReason = new HashSet<string> { Deferred, WaiverRequested };
    public static readonly IReadOnlySet<string> NeedLedgerEntry = new HashSet<string> { PaymentRecorded, Waived };

    public const int ReasonMax = 500;
    public const int KeyMax = 100;
}
