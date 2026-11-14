using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling.Checkout;

/// <summary>
/// ALV-N021 A2 (OD-22 to OD-30): the ONE rule that says whether a visit may be completed. It only READS (the appointment, its recorded outcomes, the patient's future appointments, the billing
/// ledger and the visit's completed procedures) and never writes, so the completion transaction can call it and the readiness screen shows exactly what completion will check.
///
/// Items (OD-23): treatment ended (derived: the visit reached Ready for checkout), patient departed (recorded), follow-up (booked is derived from a real future Scheduled/Confirmed appointment of the
/// patient, otherwise a recorded decision), financial (applies when the account's balance owed is above zero or a completed procedure of this visit has no live charge; OD-25). A recorded
/// "payment recorded" or "waived" resolves only while the ledger entry it names is still live (not reversed); "waiver requested" never resolves (OD-26). Visits already in Ready for checkout use the
/// same rule (OD-30).
/// </summary>
public sealed class CheckoutEvaluator(AlveraDbContext db, IPracticeClock clock)
{
    private static readonly string[] FutureVisitStates = [VisitStates.Scheduled, VisitStates.Confirmed];

    public async Task<CheckoutReadiness> EvaluateAsync(Guid appointmentId, CancellationToken ct)
    {
        var a = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == appointmentId, ct)
            ?? throw new CheckoutException("appointment_not_found", "That appointment was not found.", 404);
        var recorded = await db.VisitCheckoutOutcomes.AsNoTracking().Where(o => o.AppointmentId == appointmentId).ToListAsync(ct);
        var current = Current(recorded);
        var names = await ClinicalNames.ResolveAsync(db, recorded.Select(o => (Guid?)o.ActorUserId), ct);
        var ledger = await LedgerAsync(a.PatientId, ct);

        var treatmentEnded = a.FlowState is VisitStates.CheckedOut or VisitStates.Completed;
        var items = new List<CheckoutItemView>
        {
            new(CheckoutItemCodes.TreatmentEnded, true, treatmentEnded,
                treatmentEnded ? new CheckoutOutcomeView(null, DerivedCheckoutOutcomes.TreatmentEnded, true, null, null, null, null, a.FlowChangedAtUtc, true) : null,
                treatmentEnded ? null : "The provider ends treatment.", treatmentEnded ? null : "Booked provider", []),
            Recorded(CheckoutItems.Departure, true, current, names, ledger, "Record that the patient has left.", "Front desk"),
            await FollowUpAsync(a, current, names, ledger, ct),
        };
        var financialApplies = ledger.BalanceOwed > 0m || await HasUnchargedProcedureAsync(a.Id, ledger, ct);
        items.Add(Recorded(CheckoutItems.Financial, financialApplies, current, names, ledger,
            "Record a payment, a waiver, a payment plan, insurance to bill or a statement to send.", "Front desk or billing"));

        var open = items.Where(i => i.Applies && !i.Resolved).Select(i => i.Code).ToList();
        var isOpen = a.Status == AppointmentStatuses.Scheduled && a.FlowState == VisitStates.CheckedOut;
        return new CheckoutReadiness(a.Id, a.FlowState, isOpen, open.Count == 0, Summary(a, open), items, open);
    }

    /// <summary>The current outcome per item: the latest row that no other row supersedes.</summary>
    internal static IReadOnlyDictionary<string, VisitCheckoutOutcome> Current(IReadOnlyCollection<VisitCheckoutOutcome> rows)
    {
        var superseded = rows.Where(r => r.SupersedesId is not null).Select(r => r.SupersedesId!.Value).ToHashSet();
        return rows.Where(r => !superseded.Contains(r.Id)).GroupBy(r => r.ItemCode)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.RecordedAtUtc).ThenByDescending(r => r.Id).First());
    }

    private CheckoutItemView Recorded(string item, bool applies, IReadOnlyDictionary<string, VisitCheckoutOutcome> current, IReadOnlyDictionary<Guid, string> names, LedgerFacts ledger,
        string nextAction, string responsible)
    {
        var row = current.GetValueOrDefault(item);
        var resolves = row is not null && Resolves(row, ledger);
        var view = row is null ? null : new CheckoutOutcomeView(row.Id, row.OutcomeCode, false, row.Reason, row.LinkedLedgerEntryNumber, row.LinkedAppointmentId,
            ClinicalNames.Name(names, row.ActorUserId), row.RecordedAtUtc, resolves);
        var next = resolves || !applies ? null
            : row?.OutcomeCode == CheckoutOutcomes.WaiverRequested ? "Waiver requested: billing must post the waiver, or record another financial decision."
            : row is not null && CheckoutOutcomes.NeedLedgerEntry.Contains(row.OutcomeCode) ? "The ledger entry this relied on was reversed: record the financial decision again."
            : nextAction;
        return new CheckoutItemView(item, applies, resolves || (!applies && row is null), view, next, next is null ? null : responsible, CheckoutOutcomes.ByItem[item]);
    }

    private async Task<CheckoutItemView> FollowUpAsync(Appointment a, IReadOnlyDictionary<string, VisitCheckoutOutcome> current, IReadOnlyDictionary<Guid, string> names, LedgerFacts ledger,
        CancellationToken ct)
    {
        var booked = await db.Appointments.AsNoTracking()
            .Where(x => x.PatientId == a.PatientId && x.Id != a.Id && x.Status == AppointmentStatuses.Scheduled && FutureVisitStates.Contains(x.FlowState) && x.StartUtc > a.StartUtc)
            .OrderBy(x => x.StartUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (booked is not null)
            return new CheckoutItemView(CheckoutItems.FollowUp, true, true,
                new CheckoutOutcomeView(null, DerivedCheckoutOutcomes.FollowUpBooked, true, null, null, booked, null, null, true), null, null, CheckoutOutcomes.ByItem[CheckoutItems.FollowUp]);
        return Recorded(CheckoutItems.FollowUp, true, current, names, ledger,
            "Book the next visit, or record that none is clinically needed, that the patient declined, or a deferral with its reason.", "Front desk or the treating provider");
    }

    private static bool Resolves(VisitCheckoutOutcome row, LedgerFacts ledger) => row.OutcomeCode switch
    {
        CheckoutOutcomes.WaiverRequested => false,
        CheckoutOutcomes.PaymentRecorded or CheckoutOutcomes.Waived => row.LinkedLedgerEntryNumber is { } n && ledger.Live.Contains(n),
        _ => true,
    };

    private static string Summary(Appointment a, IReadOnlyList<string> open)
    {
        if (a.Status != AppointmentStatuses.Scheduled) return "No checkout: the appointment is not a visit.";
        if (a.FlowState == VisitStates.Completed) return "Completed.";
        if (a.FlowState != VisitStates.CheckedOut) return "Checkout opens when treatment ends.";
        if (open.Count == 0) return "Ready for checkout — every item is resolved; the visit can be completed.";
        return "Ready for checkout — " + open[0] switch
        {
            CheckoutItems.Departure => "patient departure not recorded",
            CheckoutItems.FollowUp => "follow-up decision required",
            CheckoutItems.Financial => "financial decision required",
            _ => "treatment not ended",
        } + (open.Count > 1 ? $" (and {open.Count - 1} more)" : "") + ".";
    }

    // ---------- ledger (read only) ----------

    /// <summary>What the checkout needs from the patient's own account: the balance owed, the live (unreversed) entries and the plan items with a live charge.</summary>
    internal sealed record LedgerFacts(decimal BalanceOwed, IReadOnlySet<int> Live, IReadOnlySet<Guid> ChargedPlanItems, IReadOnlyList<BillingEntry> Entries);

    internal async Task<LedgerFacts> LedgerAsync(Guid patientId, CancellationToken ct)
    {
        var entries = await db.BillingEntries.AsNoTracking().Where(e => e.PatientId == patientId).ToListAsync(ct);
        var reversed = entries.Where(e => e.ReversesEntryId is not null).Select(e => e.ReversesEntryId!.Value).ToHashSet();
        var live = entries.Where(e => !reversed.Contains(e.Id)).ToList();
        return new LedgerFacts(entries.Sum(e => e.Amount), live.Select(e => e.EntryNumber).ToHashSet(),
            live.Where(e => e.Kind == BillingKinds.Charge && e.PlanItemId is not null).Select(e => e.PlanItemId!.Value).ToHashSet(), entries);
    }

    /// <summary>OD-25: a procedure completed in this visit's encounter (its current encounter, after any amendment) has no live charge.</summary>
    private async Task<bool> HasUnchargedProcedureAsync(Guid appointmentId, LedgerFacts ledger, CancellationToken ct)
    {
        var encounters = await db.Encounters.AsNoTracking().Where(e => e.AppointmentId == appointmentId).Select(e => e.Id).ToListAsync(ct);
        if (encounters.Count == 0) return false;
        var procedures = await db.CompletedProcedures.AsNoTracking()
            .Select(p => new
            {
                p.PlanItemId,
                Encounter = db.CompletedProcedureAmendments.Where(m => m.CompletedProcedureId == p.Id && m.EncounterId != null)
                    .OrderByDescending(m => m.AmendmentNumber).Select(m => m.EncounterId).FirstOrDefault() ?? p.EncounterId,
            })
            .Where(p => encounters.Contains(p.Encounter)).Select(p => p.PlanItemId).ToListAsync(ct);
        return procedures.Any(item => !ledger.ChargedPlanItems.Contains(item));
    }

    /// <summary>The practice-local day of the visit, which a verified payment or waiver must fall on (OD-26).</summary>
    internal DateOnly VisitDay(Appointment a) => DateOnly.FromDateTime(clock.ToPracticeLocal(a.StartUtc).DateTime);

    internal DateOnly DayOf(DateTimeOffset instant) => DateOnly.FromDateTime(clock.ToPracticeLocal(instant).DateTime);
}
