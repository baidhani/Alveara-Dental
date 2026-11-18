import { useId, useState } from "react";
import { ApiError } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { planFieldErrorsOf, scheduleItem } from "../../services/treatmentPlansApi";
import type { PlanItem, TreatmentPlan } from "../../services/treatmentPlansApi";

interface Props {
  plan: TreatmentPlan;
  item: PlanItem;
  onDone: (plan: TreatmentPlan, text: string) => void;
  onCancel: () => void;
}

/**
 * ALV-N023 (owner decisions DR-1 and PD-15; F-185, F-188, F-221): an accepted procedure's TARGET date. It is a target only - it never books an appointment and never closes the "accepted treatment is
 * not booked" follow-up (only a linked appointment does). A date is needed to save; a date that has passed is saved only after the person confirms it, and is then shown as overdue. Clearing it is
 * recorded too. A refusal is shown with what was typed kept.
 */
export function TreatmentPlanScheduleForm({ plan, item, onDone, onCancel }: Props) {
  const [date, setDate] = useState(item.plannedDate ?? "");
  const [note, setNote] = useState<string | null>(null);
  const [pastPending, setPastPending] = useState(false);
  const [busy, setBusy] = useState(false);
  const id = useId();
  const hasTarget = item.scheduling === "Scheduled";

  async function save(wanted: boolean, confirmPast = false) {
    if (wanted && date === "") { setNote("Not saved: choose the target date, or clear it."); return; }
    setBusy(true);
    setNote(null);
    try {
      onDone(await scheduleItem(plan.id, item.id, wanted, wanted ? date : null, confirmPast),
        wanted ? `Target date for ${item.procedureCode} saved${confirmPast ? " (overdue)" : ""}. It does not book an appointment.` : `Target date of ${item.procedureCode} cleared.`);
    } catch (err) {
      if (err instanceof ApiError && err.code === "past_target_date") { setPastPending(true); setNote("That date has passed. Save it anyway as an overdue target date?"); return; }
      const fields = Object.values(planFieldErrorsOf(err));
      if (fields.length > 0) setNote(`Not saved: ${fields.join(" ")}`);
      else if (isNetworkFailure(err)) setNote("Not saved: the connection dropped. Try again.");
      else setNote(`Not saved: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label={`Target date of ${item.procedureCode}`} onSubmit={(e) => { e.preventDefault(); void save(true); }}>
      <p className="alv-clinical__meta">A target date is only a plan for when {item.procedureCode} should happen. It does not book an appointment; link an appointment to book it.</p>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      <label htmlFor={id}>Target date
        <input id={id} type="date" value={date} onChange={(e) => { setDate(e.target.value); setNote(null); setPastPending(false); }} />
      </label>
      <div className="alv-clinical__row-actions">
        {pastPending
          ? <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void save(true, true)}>Save as overdue</button>
          : <button type="submit" className="btn btn-primary" disabled={busy}>Save target date</button>}
        {hasTarget && <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => void save(false)}>Clear target date</button>}
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
