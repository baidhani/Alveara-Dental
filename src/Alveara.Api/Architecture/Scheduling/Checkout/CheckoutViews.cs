namespace Alveara.Api.Architecture.Scheduling.Checkout;

/// <summary>ALV-N021 A2 (OD-23): the item codes the readiness response uses. Treatment ended is derived; the other three are <see cref="CheckoutItems"/>.</summary>
public static class CheckoutItemCodes
{
    public const string TreatmentEnded = "treatment_ended";
    public static readonly IReadOnlyList<string> InOrder = [TreatmentEnded, CheckoutItems.Departure, CheckoutItems.FollowUp, CheckoutItems.Financial];
}

/// <summary>The derived outcome codes (never recorded by hand).</summary>
public static class DerivedCheckoutOutcomes
{
    public const string TreatmentEnded = "treatment_ended";
    public const string FollowUpBooked = "booked";
}

/// <summary>The outcome an item currently has: recorded (with who and when) or derived (no actor).</summary>
public sealed record CheckoutOutcomeView(
    Guid? Id, string Code, bool Derived, string? Reason, int? LinkedLedgerEntryNumber, Guid? LinkedAppointmentId, string? RecordedByName, DateTimeOffset? RecordedAtUtc, bool Resolves);

/// <summary>One checkout item: whether it applies, whether it is resolved, its current outcome, what must happen next and who does it (plain words; never colour alone).</summary>
public sealed record CheckoutItemView(string Code, bool Applies, bool Resolved, CheckoutOutcomeView? Outcome, string? NextAction, string? ResponsibleRole, IReadOnlyList<string> RecordableOutcomes);

/// <summary>
/// The readiness of a visit for completion (GET appointments/{id}/checkout). <see cref="Ready"/> is true only when every applicable item is resolved; <see cref="Summary"/> is the one line Calendar and
/// Board show ("Ready for checkout — financial decision required"). <see cref="Open"/> says whether outcomes may be recorded now (the visit is ready for checkout).
/// </summary>
public sealed record CheckoutReadiness(Guid AppointmentId, string FlowState, bool Open, bool Ready, string Summary, IReadOnlyList<CheckoutItemView> Items, IReadOnlyList<string> OpenItemCodes);

/// <summary>POST appointments/{id}/checkout/{item}: the outcome to record. <see cref="LedgerEntryNumber"/> names the account's payment or adjustment a verified outcome relies on.</summary>
public sealed record RecordCheckoutOutcomeRequest(string? Outcome, string? Reason, int? LedgerEntryNumber);

/// <summary>A checkout request was refused. <see cref="Code"/> is stable for the UI; <see cref="OpenItems"/> lists the unresolved item codes where that helps.</summary>
public sealed class CheckoutException(string code, string message, int statusCode, IReadOnlyList<string>? openItems = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyList<string>? OpenItems { get; } = openItems;
}
