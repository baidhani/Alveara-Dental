import { useEffect, useId, useState } from "react";
import { getBookableTreatment } from "../../services/schedulingApi";
import type { BookableTreatment } from "../../services/schedulingApi";
import { bookableLabel } from "./bookableTreatmentText";

interface Props {
  patientId: string | null;
  selected: string[];
  onChange: (itemIds: string[]) => void;
  disabled: boolean;
}

/**
 * ALV-N023 (owner decision OQ-1): while booking, the front desk may name the patient's accepted procedures that this appointment books. Only accepted, unbooked procedures are offered, and only
 * with what scheduling needs (no plan title, no diagnosis). Booking them links them to the appointment in the same save; nothing about the plan itself changes.
 */
export function BookableTreatmentField({ patientId, selected, onChange, disabled }: Props) {
  const id = useId();
  const [items, setItems] = useState<BookableTreatment[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!patientId) return;
    const controller = new AbortController();
    getBookableTreatment(patientId, controller.signal)
      .then((r) => { setItems(r.items); setFailed(false); })
      .catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [patientId]);

  if (!patientId) return null;
  const toggle = (itemId: string) => onChange(selected.includes(itemId) ? selected.filter((x) => x !== itemId) : [...selected, itemId]);
  return (
    <fieldset className="alv-form-field" aria-describedby={`${id}-hint`}>
      <legend className="alv-form-field__label">For accepted treatment (optional)</legend>
      <p id={`${id}-hint`} className="alv-clinical__meta">Tick the accepted procedures this appointment is for. They are then shown as booked on the patient&apos;s treatment plan.</p>
      {failed ? <p className="alv-form-field__error" role="alert">The patient&apos;s accepted treatment could not be loaded. You can still book the appointment.</p>
        : items === null ? <p className="alv-clinical__meta">Loading accepted treatment…</p>
        : items.length === 0 ? <p className="alv-clinical__meta">This patient has no accepted treatment waiting to be booked.</p>
        : items.map((t) => (
          <label key={t.itemId} className="alv-form-field__check">
            <input type="checkbox" checked={selected.includes(t.itemId)} onChange={() => toggle(t.itemId)} disabled={disabled} /> {bookableLabel(t)}
          </label>
        ))}
    </fieldset>
  );
}
