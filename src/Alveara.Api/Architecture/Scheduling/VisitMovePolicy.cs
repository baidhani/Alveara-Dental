using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>Who is asking for a visit move: the signed-in account and its role.</summary>
public sealed record VisitActor(Guid UserId, Role Role);

/// <summary>The two practice settings of the visit workflow (briefing 5.2). Both default to required, also when the practice has no settings row yet.</summary>
public sealed record VisitSettings(bool ConfirmationRequired, bool MarkReadyRequired)
{
    public static readonly VisitSettings Default = new(true, true);

    public static async Task<VisitSettings> ReadAsync(AlveraDbContext db, CancellationToken ct) =>
        await db.PracticeSettings.AsNoTracking().Select(s => new VisitSettings(s.ConfirmationRequired, s.MarkReadyRequired)).FirstOrDefaultAsync(ct) ?? Default;
}

/// <summary>
/// ALV-N021 A2 (briefing 5.4, OD-01 to OD-12, OD-29): WHO may make a visit move, decided on the server for every route (the visit board's <c>visits/{id}/state</c> and the legacy
/// <c>appointments/{id}/check-in|start-treatment|complete</c>), so no route is looser than another.
///
/// - Confirm, check in: <see cref="Permission.UpdateVisitFlow"/> (front desk, office manager, administrator).
/// - Mark ready, seat: <see cref="Permission.UpdateChairsideFlow"/> (dentist, hygienist, assistant, administrator).
/// - Start and end treatment: the chairside permission AND the actor is a Dentist, Hygienist or Administrator whose account is linked to the appointment's BOOKED provider ("own"). An administrator is
///   bound by this too (OD-08).
/// - Complete: <see cref="Permission.UpdateVisitFlow"/>; or <see cref="Permission.CompleteOwnStartedVisit"/> held by a Dentist or Hygienist linked to a provider record whose account recorded THIS
///   visit's start of treatment (OD-09). Any other role is denied even if a grant to it were somehow present. The checkout gate is separate and applies to everyone (OD-29).
/// Refusals are 403 <c>visit_move_forbidden</c> with a reason code; nothing is written.
/// </summary>
public sealed class VisitMovePolicy(AlveraDbContext db, IEffectivePermissions permissions)
{
    /// <summary>The move that is not a state of the path: undo check-in (OD-11), front-desk work like check-in itself.</summary>
    public const string UndoCheckIn = "UndoCheckIn";
    /// <summary>OD-18 Keep despite blocked time: front-desk work (briefing 5.4 table).</summary>
    public const string KeepDespiteBlock = "KeepDespiteBlock";

    public async Task AuthorizeAsync(Appointment a, string target, VisitActor actor, CancellationToken ct)
    {
        var reason = await RefusalAsync(a, target, actor, ct);
        if (reason is not null) throw new SchedulingException("visit_move_forbidden", Message(reason), 403, reason: reason);
    }

    /// <summary>
    /// OD-13, OD-16: changing the booked provider needs <see cref="Permission.ReassignAppointmentProvider"/>; changing only the room needs that or <see cref="Permission.UpdateChairsideFlow"/>.
    /// </summary>
    public async Task AuthorizeAssignmentAsync(bool providerChanges, VisitActor actor, CancellationToken ct)
    {
        if (await permissions.HasAsync(actor.Role, Permission.ReassignAppointmentProvider, ct)) return;
        var reason = providerChanges ? "reassign_not_permitted"
            : await permissions.HasAsync(actor.Role, Permission.UpdateChairsideFlow, ct) ? null : "room_change_not_permitted";
        if (reason is not null) throw new SchedulingException("visit_move_forbidden", Message(reason), 403, reason: reason);
    }

    private async Task<string?> RefusalAsync(Appointment a, string target, VisitActor actor, CancellationToken ct)
    {
        switch (target)
        {
            case VisitStates.Confirmed or VisitStates.CheckedIn or UndoCheckIn or KeepDespiteBlock:
                return await permissions.HasAsync(actor.Role, Permission.UpdateVisitFlow, ct) ? null : "front_office_only";
            case VisitStates.Ready or VisitStates.Seated:
                return await permissions.HasAsync(actor.Role, Permission.UpdateChairsideFlow, ct) ? null : "chairside_only";
            case VisitStates.InTreatment or VisitStates.CheckedOut:
                if (!await permissions.HasAsync(actor.Role, Permission.UpdateChairsideFlow, ct) || actor.Role is not (Role.Dentist or Role.Hygienist or Role.Admin)) return "booked_provider_only";
                return (await ProvidersOfAsync(actor.UserId, ct)).Contains(a.ProviderProfileId) ? null : "booked_provider_only";
            case VisitStates.Completed:
                if (await permissions.HasAsync(actor.Role, Permission.UpdateVisitFlow, ct)) return null;
                if (actor.Role is not (Role.Dentist or Role.Hygienist) || !await permissions.HasAsync(actor.Role, Permission.CompleteOwnStartedVisit, ct)) return "complete_not_permitted";
                if ((await ProvidersOfAsync(actor.UserId, ct)).Count == 0) return "not_linked_to_a_provider";
                var started = await db.AppointmentEvents.AsNoTracking()
                    .AnyAsync(e => e.AppointmentId == a.Id && e.EventType == AppointmentEventTypes.TreatmentStarted && e.ActorUserId == actor.UserId, ct);
                return started ? null : "not_the_one_who_started_treatment";
            default:
                return "unknown_move";
        }
    }

    /// <summary>The active provider records linked to an account (through its staff profile).</summary>
    public async Task<IReadOnlyList<Guid>> ProvidersOfAsync(Guid userId, CancellationToken ct) =>
        await db.ProviderProfiles.AsNoTracking().Where(p => p.IsActive && p.StaffProfile != null && p.StaffProfile.UserAccountId == userId).Select(p => p.Id).ToListAsync(ct);

    private static string Message(string reason) => reason switch
    {
        "front_office_only" => "Confirming, checking in, undoing a check-in and keeping an appointment despite blocked time are front-desk work.",
        "reassign_not_permitted" => "You do not have permission to change an appointment's provider.",
        "room_change_not_permitted" => "You do not have permission to change an appointment's room.",
        "chairside_only" => "Marking ready and seating are chairside work.",
        "booked_provider_only" => "Only the appointment's booked provider can start or end treatment.",
        "complete_not_permitted" => "Your role may not complete this visit.",
        "not_linked_to_a_provider" => "Your account is not linked to a provider record, so you cannot complete a visit you treated.",
        "not_the_one_who_started_treatment" => "You can complete only a visit whose treatment you started.",
        _ => "That move is not allowed.",
    };
}
