namespace Alveara.Api.Architecture.Treatment;

/// <summary>ALV-N023 (DR-1): what a link row records. A link is never edited: a new appointment is a new Linked row; removing one is an Unlinked row with a reason.</summary>
public static class AppointmentLinkActions
{
    public const string Linked = "Linked";
    public const string Unlinked = "Unlinked";
    public static readonly IReadOnlyList<string> All = [Linked, Unlinked];
}

/// <summary>
/// ALV-N023 (owner decisions DR-1 and OQ-1; briefing revision 2 section 4): the append-only history of which real appointment books an accepted plan item. The item's <b>current link</b> is its
/// highest-sequence row when that row is Linked; it is <b>live</b> while the appointment's status is Scheduled (Cancelled and NoShow are terminal for an appointment). Nothing here changes the
/// appointment: a move is followed because readers take the appointment's own start, provider and status. The database refuses a link to another patient's appointment (51291) and any update
/// or delete (51292); rolling the migration back is refused once a row exists (51293).
/// </summary>
public class TreatmentPlanItemAppointmentLink
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid ItemId { get; set; }
    /// <summary>The item's patient; the database checks the appointment belongs to the same patient.</summary>
    public Guid PatientId { get; set; }
    /// <summary>The appointment linked (Linked) or the one whose link was removed (Unlinked).</summary>
    public Guid AppointmentId { get; set; }
    /// <summary>1, 2, 3... per item; unique per item, so two simultaneous changes cannot both land.</summary>
    public int Sequence { get; set; }
    /// <summary>One of <see cref="AppointmentLinkActions.All"/>.</summary>
    public required string Action { get; set; }
    /// <summary>Required for Unlinked (staff wording, kept out of the audit log); null for Linked.</summary>
    public string? Reason { get; set; }
    public Guid RecordedByUserId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>ALV-N023: an item's current appointment as a screen shows it. <see cref="Live"/> is false once the appointment is cancelled or marked no-show; the item is then not booked.</summary>
public sealed record ItemAppointmentView(Guid AppointmentId, DateTimeOffset StartUtc, string? ProviderName, string Status, bool Live, string? LinkedByName, DateTimeOffset LinkedAtUtc);

/// <summary>ALV-N023: what a link or unlink returns. Deliberately minimal (no plan title, diagnosis or other clinical text): front desk and office manager may link but cannot read plans.</summary>
public sealed record ItemAppointmentResult(Guid PlanId, Guid ItemId, ItemAppointmentView? Appointment);

/// <summary>
/// ALV-N023 (OQ-1): one accepted procedure as the front desk sees it when booking, or on an appointment that books it - only what scheduling needs: the procedure code and description, where on
/// the mouth, the item number and the plan's reference code. No plan title, no diagnosis, no clinical text (front desk cannot read plans).
/// </summary>
public sealed record BookableTreatmentView(Guid PlanId, Guid ItemId, int ItemNumber, string ProcedureCode, string ProcedureDescription, string? ToothKey, string? Surface, string? PlanReference);
