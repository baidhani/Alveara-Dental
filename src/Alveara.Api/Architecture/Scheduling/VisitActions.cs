using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Scheduling.Checkout;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>Stable codes of the actions a screen can offer on an appointment. The client keys its buttons and dialogs on these, never on its own rules.</summary>
public static class VisitActionCodes
{
    public const string Confirm = "confirm";
    public const string CheckIn = "check_in";
    public const string UndoCheckIn = "undo_check_in";
    public const string MarkReady = "mark_ready";
    public const string Seat = "seat";
    public const string StartTreatment = "start_treatment";
    public const string EndTreatment = "end_treatment";
    public const string Complete = "complete";
    public const string Reschedule = "reschedule";
    public const string Cancel = "cancel";
    public const string NoShow = "no_show";
    public const string ReassignProvider = "reassign_provider";
    public const string ChangeRoom = "change_room";
    public const string KeepDespiteBlock = "keep_despite_block";
    // ALV-N021 A2 (briefing 5.4, F-045, F-051, F-073): after a cancellation or a no-show, book the patient again (a NEW appointment through the normal booking rules) or open their record
    public const string Rebook = "rebook";
    public const string OpenPatient = "open_patient";
}

/// <summary>
/// One action the signed-in user may take now. <see cref="Enabled"/> false means the user holds the authority but a rule of the appointment blocks it at this moment
/// (<see cref="BlockedBy"/> is the same error code the route would return, <see cref="BlockedText"/> says why in words); actions the user has no authority for are not listed at all.
/// <see cref="TargetState"/> is set for moves along the visit path (sent to <c>POST visits/{id}/state</c>); <see cref="NeedsReason"/> tells the dialog to ask for one.
/// </summary>
public sealed record VisitAction(string Code, string Label, bool Enabled, string? BlockedBy, string? BlockedText, string? TargetState, bool NeedsReason);

/// <summary>GET <c>appointments/{id}/actions</c> (briefing 5.5): what the signed-in user can do to this appointment now, and who the visit is waiting for.</summary>
public sealed record AppointmentActions(Guid AppointmentId, string RowVersion, string Status, string FlowState, bool BlockFlagged, string? NextActorText, IReadOnlyList<VisitAction> Actions);

/// <summary>
/// ALV-N021 A2 (briefing 5.5, "available actions"): the ONE place the Calendar and the Board learn which buttons to show. Read-only: it asks the same questions the routes ask
/// (<see cref="VisitMovePolicy"/> for who, <see cref="VisitStateMachine"/> with the practice settings for where, and the route rules for when: the check-in day rule, the block flag,
/// the checkout gate, the no-show start-time rule), so a button it offers is one the server accepts, and a move the server refuses is never offered as enabled.
/// The server still decides every request on its own; this list is guidance for the screen, not a permission.
/// </summary>
public sealed class VisitActionsService(AlveraDbContext db, IPracticeClock clock, IEffectivePermissions permissions)
{
    private static readonly (string Target, string Code, string Label)[] PathMoves =
    [
        (VisitStates.Confirmed, VisitActionCodes.Confirm, "Confirm"),
        (VisitStates.CheckedIn, VisitActionCodes.CheckIn, "Check in"),
        (VisitStates.Ready, VisitActionCodes.MarkReady, "Mark ready"),
        (VisitStates.Seated, VisitActionCodes.Seat, "Seat patient"),
        (VisitStates.InTreatment, VisitActionCodes.StartTreatment, "Start treatment"),
        (VisitStates.CheckedOut, VisitActionCodes.EndTreatment, "End treatment / ready for checkout"),
        (VisitStates.Completed, VisitActionCodes.Complete, "Complete visit"),
    ];

    private static readonly string[] BeforeArrival = [VisitStates.Scheduled, VisitStates.Confirmed];
    private static readonly string[] Reassignable = [VisitStates.Scheduled, VisitStates.Confirmed, VisitStates.CheckedIn, VisitStates.Ready, VisitStates.Seated];

    public async Task<AppointmentActions> ForAsync(Guid appointmentId, VisitActor actor, CancellationToken ct)
    {
        var a = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == appointmentId, ct)
                ?? throw new SchedulingException("appointment_not_found", "No appointment with that id.", 404);
        return await ForAsync(a, actor, await VisitSettings.ReadAsync(db, ct), ct);
    }

    /// <summary>For a loaded appointment, with the settings read once by the caller (the board builds one list per card).</summary>
    public async Task<AppointmentActions> ForAsync(Appointment a, VisitActor actor, VisitSettings settings, CancellationToken ct)
    {
        var policy = new VisitMovePolicy(db, permissions);
        var flagged = await BlockFlags.IsFlaggedAsync(db, a, ct);
        var version = Convert.ToBase64String(a.RowVersion);
        if (a.Status != AppointmentStatuses.Scheduled || a.FlowState == VisitStates.Completed)
            return new AppointmentActions(a.Id, version, a.Status, a.FlowState, false, null, await AfterEndAsync(a, actor, policy, ct));

        var actions = new List<VisitAction>();
        var checkout = a.FlowState == VisitStates.CheckedOut ? await new CheckoutEvaluator(db, clock).EvaluateAsync(a.Id, ct) : null;
        foreach (var (target, code, label) in PathMoves)
        {
            if (VisitStateMachine.Decide(a.FlowState, target, settings) != FlowDecision.Move) continue;
            if (await policy.MoveRefusalAsync(a, target, actor, ct) is not null) continue;
            var (blockedBy, text) = (default(string), default(string));
            if (target == VisitStates.CheckedIn && flagged)
                (blockedBy, text) = ("block_flag_unresolved", "The provider has blocked this time. Reschedule, reassign, cancel or keep the appointment first.");
            else if (target == VisitStates.CheckedIn && DayOf(a.StartUtc) != DayOf(clock.UtcNow))
                (blockedBy, text) = ("check_in_wrong_day", "A patient can be checked in only on the day of the appointment.");
            else if (target == VisitStates.Completed && checkout is { Ready: false })
                (blockedBy, text) = ("checkout_incomplete", checkout.Summary);
            actions.Add(new VisitAction(code, label, blockedBy is null, blockedBy, text, target, false));
        }

        if (a.FlowState is VisitStates.CheckedIn or VisitStates.Ready && await policy.MoveRefusalAsync(a, VisitMovePolicy.UndoCheckIn, actor, ct) is null)
            actions.Add(new VisitAction(VisitActionCodes.UndoCheckIn, "Undo check-in", true, null, null, null, true));

        if (BeforeArrival.Contains(a.FlowState))
        {
            if (flagged && await policy.MoveRefusalAsync(a, VisitMovePolicy.KeepDespiteBlock, actor, ct) is null)
                actions.Add(new VisitAction(VisitActionCodes.KeepDespiteBlock, "Keep despite blocked time", true, null, null, null, true));
            if (await policy.HasAsync(actor, Permission.ManageAppointments, ct))
            {
                actions.Add(new VisitAction(VisitActionCodes.Reschedule, "Reschedule", true, null, null, null, false));
                actions.Add(new VisitAction(VisitActionCodes.Cancel, "Cancel", true, null, null, null, false));   // the dialog asks for a category (required) and a note
                var early = clock.UtcNow < a.StartUtc;
                actions.Add(new VisitAction(VisitActionCodes.NoShow, "Mark no-show", !early, early ? "no_show_too_early" : null,
                    early ? "An appointment can only be marked as a no-show once its start time has passed." : null, null, false));
            }
        }

        if (Reassignable.Contains(a.FlowState))
        {
            if (await policy.AssignmentRefusalAsync(true, actor, ct) is null)
                actions.Add(new VisitAction(VisitActionCodes.ReassignProvider, "Change provider", true, null, null, null, true));
            if (await policy.AssignmentRefusalAsync(false, actor, ct) is null)
                actions.Add(new VisitAction(VisitActionCodes.ChangeRoom, "Change room", true, null, null, null, false));
        }

        return new AppointmentActions(a.Id, version, a.Status, a.FlowState, flagged, NextActorText(a, flagged, settings, checkout), actions);
    }

    /// <summary>
    /// A cancelled or no-show appointment changes no more, but the people who book (briefing 5.4: front desk, office manager, administrator through <see cref="Permission.ManageAppointments"/>)
    /// are offered to book the patient again - a new appointment, decided by the booking route's own rules - and to open the patient's record (which also needs <see cref="Permission.ViewPatientRecords"/>).
    /// A completed visit offers nothing.
    /// </summary>
    private static async Task<IReadOnlyList<VisitAction>> AfterEndAsync(Appointment a, VisitActor actor, VisitMovePolicy policy, CancellationToken ct)
    {
        if (a.Status is not (AppointmentStatuses.Cancelled or AppointmentStatuses.NoShow) || !await policy.HasAsync(actor, Permission.ManageAppointments, ct)) return [];
        var actions = new List<VisitAction> { new(VisitActionCodes.Rebook, "Rebook", true, null, null, null, false) };
        if (await policy.HasAsync(actor, Permission.ViewPatientRecords, ct)) actions.Add(new VisitAction(VisitActionCodes.OpenPatient, "Open patient", true, null, null, null, false));
        return actions;
    }

    /// <summary>Who the visit is waiting for, in words (briefing 5.5; announced through the screen's live region). The provider is named by role, not by person, so the text stays PHI-free.</summary>
    internal static string? NextActorText(Appointment a, bool flagged, VisitSettings settings, CheckoutReadiness? checkout)
    {
        if (flagged) return "Blocked time covers this appointment: the front desk must reschedule, reassign, cancel or keep it.";
        return a.FlowState switch
        {
            VisitStates.Scheduled => settings.ConfirmationRequired ? "Waiting for the front desk to confirm." : "Waiting for the front desk to check the patient in.",
            VisitStates.Confirmed => "Waiting for the front desk to check the patient in.",
            VisitStates.CheckedIn => settings.MarkReadyRequired ? "Waiting for chairside staff to mark the patient ready." : "Waiting for chairside staff to seat the patient.",
            VisitStates.Ready => "Waiting for chairside staff to seat the patient.",
            VisitStates.Seated => "Waiting for the booked provider to start treatment.",
            VisitStates.InTreatment => "In treatment; the booked provider ends treatment.",
            VisitStates.CheckedOut => checkout?.Summary ?? "Ready for checkout.",   // the evaluator's own sentence, e.g. "Ready for checkout — financial decision required."
            _ => null,
        };
    }

    private DateOnly DayOf(DateTimeOffset instant) => DateOnly.FromDateTime(clock.ToPracticeLocal(instant).DateTime);
}
