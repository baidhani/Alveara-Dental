import { useEffect, useId, useState } from "react";
import { ApiError } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { getLinkableAppointments, linkPlanItemAppointment, listTreatmentPlans, planFieldErrorsOf, unlinkPlanItemAppointment } from "../../services/treatmentPlansApi";
import type { LinkableAppointment, PlanItem, TreatmentPlan } from "../../services/treatmentPlansApi";
import { when } from "./treatmentPlanRules";

interface Props {
  plan: TreatmentPlan;
  item: PlanItem;
  onDone: (plan: TreatmentPlan, text: string) => void;
  onCancel: () => void;
}

/** After a link or unlink (whose answer is deliberately minimal), the plan is read again so the screen shows what the server now holds. */
async function reloaded(plan: TreatmentPlan): Promise<TreatmentPlan> {
  const { plans } = await listTreatmentPlans(plan.patientId, { includeWithdrawn: true });
  return plans.find((p) => p.id === plan.id) ?? plan;
}

/**
 * ALV-N023 (owner decision DR-1): book an accepted procedure by linking one of the patient's real, still-scheduled appointments, or remove the link with a reason. A target date is separate and never
 * books anything. The appointment itself is not changed; a later move is followed and a cancellation or no-show shows the procedure as not booked.
 */
export function TreatmentPlanAppointmentLink({ plan, item, onDone, onCancel }: Props) {
  const id = useId();
  const linked = item.appointment?.live ? item.appointment : null;
  const [appointments, setAppointments] = useState<LinkableAppointment[] | null>(null);
  const [chosen, setChosen] = useState("");
  const [reason, setReason] = useState("");
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (linked) return;
    const controller = new AbortController();
    getLinkableAppointments(plan.patientId, controller.signal).then((r) => setAppointments(r.appointments)).catch(() => { if (!controller.signal.aborted) setAppointments([]); });
    return () => controller.abort();
  }, [plan.patientId, linked]);

  async function act(run: () => Promise<unknown>, text: string) {
    setBusy(true);
    setNote(null);
    try {
      await run();
      onDone(await reloaded(plan), text);
    } catch (err) {
      const fields = Object.values(planFieldErrorsOf(err));
      if (fields.length > 0) setNote(`Not saved: ${fields.join(" ")}`);
      else if (isNetworkFailure(err)) setNote("Not saved: the connection dropped. Try again.");
      else setNote(`Not saved: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  if (linked) {
    return (
      <form className="alv-dx__form" aria-label={`Unlink the appointment of ${item.procedureCode}`} onSubmit={(e) => {
        e.preventDefault();
        if (reason.trim() === "") return setNote("Say why.");
        void act(() => unlinkPlanItemAppointment(plan.id, item.id, reason.trim()), `${item.procedureCode} is no longer booked by that appointment.`);
      }}>
        <p className="alv-clinical__meta">{item.procedureCode} is booked by the appointment on {when(linked.startUtc)}{linked.providerName ? ` with ${linked.providerName}` : ""}. Unlinking does not cancel the appointment.</p>
        {note && <p className="alv-dx__message" role="alert">{note}</p>}
        <label htmlFor={`${id}-why`}>Why is this appointment no longer booking the procedure?
          <input id={`${id}-why`} type="text" maxLength={500} value={reason} onChange={(e) => { setReason(e.target.value); setNote(null); }} />
        </label>
        <div className="alv-clinical__row-actions">
          <button type="submit" className="btn btn-primary" disabled={busy}>Unlink appointment</button>
          <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
        </div>
      </form>
    );
  }

  return (
    <form className="alv-dx__form" aria-label={`Link an appointment to ${item.procedureCode}`} onSubmit={(e) => {
      e.preventDefault();
      if (!chosen) return setNote("Choose the appointment that books this procedure.");
      void act(() => linkPlanItemAppointment(plan.id, item.id, chosen), `${item.procedureCode} is booked.`);
    }}>
      <p className="alv-clinical__meta">Choose one of the patient&apos;s scheduled appointments. To book a new one, use the calendar first.</p>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      {appointments === null ? <p className="alv-clinical__meta">Loading the patient&apos;s appointments…</p>
        : appointments.length === 0 ? <p className="alv-clinical__meta">This patient has no scheduled appointment to link. Book one in the calendar first.</p>
        : (
          <label htmlFor={`${id}-appt`}>Appointment
            <select id={`${id}-appt`} value={chosen} onChange={(e) => { setChosen(e.target.value); setNote(null); }}>
              <option value="">Choose an appointment</option>
              {appointments.map((a) => <option key={a.appointmentId} value={a.appointmentId}>{when(a.startUtc)}{a.providerName ? ` with ${a.providerName}` : ""} ({a.durationMinutes} min)</option>)}
            </select>
          </label>
        )}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || !appointments?.length}>Link appointment</button>
        <a className="btn btn-outline-secondary" href="/calendar">Book in calendar</a>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
