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
 * ALV-015-C01: mark an accepted procedure scheduled, with an optional planned date, or take it off the schedule. This records a state only; it does not book an appointment. Each change is added to the
 * history, and a refusal (the procedure is not accepted, or already completed) is shown with what was typed kept.
 */
export function TreatmentPlanScheduleForm({ plan, item, onDone, onCancel }: Props) {
  const [date, setDate] = useState(item.plannedDate ?? "");
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const id = useId();
  const scheduled = item.scheduling === "Scheduled";

  async function save(wanted: boolean) {
    setBusy(true);
    setNote(null);
    try {
      onDone(await scheduleItem(plan.id, item.id, wanted, wanted ? date || null : null), wanted ? `${item.procedureCode} scheduled.` : `${item.procedureCode} taken off the schedule.`);
    } catch (err) {
      const fields = Object.values(planFieldErrorsOf(err));
      if (fields.length > 0) setNote(`Not saved: ${fields.join(" ")}`);
      else if (isNetworkFailure(err)) setNote("Not saved: the connection dropped. Try again.");
      else setNote(`Not saved: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label={`Schedule ${item.procedureCode}`} onSubmit={(e) => { e.preventDefault(); void save(true); }}>
      <p className="alv-clinical__meta">This records that {item.procedureCode} is scheduled. It does not book an appointment.</p>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      <label htmlFor={id}>Planned date (optional)
        <input id={id} type="date" value={date} onChange={(e) => { setDate(e.target.value); setNote(null); }} />
      </label>
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Save schedule</button>
        {scheduled && <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => void save(false)}>Take off the schedule</button>}
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
