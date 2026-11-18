import { useEffect, useId, useState } from "react";
import { ApiError } from "../../services/authApi";
import { getAppointmentTreatment } from "../../services/schedulingApi";
import type { BookableTreatment } from "../../services/schedulingApi";
import { unlinkPlanItemAppointment } from "../../services/treatmentPlansApi";
import { bookableLabel } from "./bookableTreatmentText";

/**
 * ALV-N023 (owner decision OQ-1): the accepted procedures this appointment books, as the front desk may see them (code, description, tooth, plan reference - no plan title or diagnosis). A person who
 * manages appointments can remove one with a reason; the procedure is then not booked again, and its follow-up comes back. Unlinking never cancels the appointment.
 */
export function AppointmentTreatment({ appointmentId, canManage }: { appointmentId: string; canManage: boolean }) {
  const id = useId();
  const [items, setItems] = useState<BookableTreatment[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [unlinking, setUnlinking] = useState<BookableTreatment | null>(null);
  const [reason, setReason] = useState("");
  const [note, setNote] = useState<string | null>(null);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    getAppointmentTreatment(appointmentId, controller.signal).then((r) => { setItems(r.items); setFailed(false); })
      .catch((err) => { if (!controller.signal.aborted) { setFailed(!(err instanceof ApiError && err.status === 403)); setItems([]); } });
    return () => controller.abort();
  }, [appointmentId, tick]);

  async function unlink() {
    if (!unlinking) return;
    if (reason.trim() === "") return setNote("Say why.");
    try {
      await unlinkPlanItemAppointment(unlinking.planId, unlinking.itemId, reason.trim());
      setNote(`${unlinking.procedureCode} is no longer booked by this appointment.`);
      setUnlinking(null);
      setReason("");
      setTick((t) => t + 1);
    } catch (err) {
      setNote(`Not unlinked: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    }
  }

  if (failed) return <p className="alv-dx__message" role="alert">The treatment this appointment books could not be loaded.</p>;
  if (items === null || (items.length === 0 && !note)) return null;
  return (
    <section aria-label="Treatment this appointment books">
      <h4>Treatment this appointment books</h4>
      {note && <p className="alv-clinical__status" role="status">{note}</p>}
      <ul>
        {items.map((t) => (
          <li key={t.itemId}>
            {bookableLabel(t)}{" "}
            {canManage && unlinking?.itemId !== t.itemId && <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => { setUnlinking(t); setNote(null); }}>Unlink {t.procedureCode}</button>}
          </li>
        ))}
      </ul>
      {unlinking && (
        <form aria-label={`Unlink ${unlinking.procedureCode}`} onSubmit={(e) => { e.preventDefault(); void unlink(); }}>
          <label htmlFor={`${id}-why`}>Why is this appointment no longer booking {unlinking.procedureCode}?
            <input id={`${id}-why`} type="text" maxLength={500} value={reason} onChange={(e) => { setReason(e.target.value); setNote(null); }} />
          </label>
          <button type="submit" className="btn btn-primary btn-sm">Unlink</button>{" "}
          <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => { setUnlinking(null); setReason(""); }}>Cancel</button>
        </form>
      )}
    </section>
  );
}
