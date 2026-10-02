import { useEffect, useId, useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import type { SchedulingSnapshot } from "../../services/configApi";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import {
  cancelAppointment, checkInPatient, completeTreatment, dateOf, getAppointment, getAppointmentHistory, markNoShow, rescheduleAppointment, startTreatment, timeOf,
  updateAppointmentNotes,
} from "../../services/schedulingApi";
import type { Appointment, AppointmentEvent } from "../../services/schedulingApi";
import { flowWord, statusWord } from "./calendarLayout";
import { explainRefusal } from "./explainRefusal";
import { PlacementFields } from "./PlacementFields";
import type { Placement } from "./PlacementFields";

interface Props {
  appointment: Appointment;
  snapshot: SchedulingSnapshot;
  canManage: boolean;
  /** "now" in the practice's time zone as yyyy-MM-ddTHH:mm: a no-show is only offered once the start time has passed (the server enforces it too). */
  nowLocal: string;
  onChanged: (appointment: Appointment) => void;
}

type Mode = "details" | "reschedule" | "cancel";

const EVENT_LABELS: Record<string, string> = { Scheduled: "Scheduled", Rescheduled: "Rescheduled", Cancelled: "Cancelled", NoShow: "Marked as a no-show", NotesChanged: "Note changed",
  CheckedIn: "Patient checked in", TreatmentStarted: "Treatment started", Completed: "Treatment completed",
  Confirmed: "Confirmed", Ready: "Marked ready", Seated: "Seated", CheckedOut: "Checked out", AssignmentChanged: "Room or provider changed",
};

/**
 * ALV-004-C01: one appointment in the drawer - its details and history, and (for staff who may manage appointments) reschedule, cancel with a reason,
 * mark as a no-show, and edit the note. Every change carries the version this panel last loaded: if someone else changed the appointment meanwhile the
 * change is refused and the user is told (never applied on top), and Reload shows the current appointment. Cancelled and no-show appointments stay
 * readable here with their reason, who/when and full history.
 *
 * STORY-011 adds the patient's flow through the visit: Check in, Start treatment and Complete treatment (each also carrying the version). Once the patient
 * has checked in the appointment can no longer be rescheduled, cancelled or marked as a no-show, so those buttons are not offered (the server refuses too).
 * ALV-011-C01 extended the visit with Confirmed (not yet arrived, so it can still be moved or cancelled), Ready, Seated and Checked out. This drawer keeps
 * STORY-011's three moves; the fuller chain (and reassigning the room or provider) is done on the Visit board, which the drawer points to.
 */
export function AppointmentPanel({ appointment, snapshot, canManage, nowLocal, onChanged }: Props) {
  const [current, setCurrent] = useState(appointment);
  const [mode, setMode] = useState<Mode>("details");
  const [place, setPlace] = useState<Placement>(() => placementOf(appointment));
  const [reason, setReason] = useState("");
  const [note, setNote] = useState(appointment.notes ?? "");
  const [history, setHistory] = useState<AppointmentEvent[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [lines, setLines] = useState<string[]>([]);
  const [saved, setSaved] = useState<string | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const reasonId = useId();
  const noteId = useId();

  const version = current.rowVersion ?? "";
  useEffect(() => {
    const controller = new AbortController();
    getAppointmentHistory(current.id, controller.signal)
      .then(setHistory)
      .catch(() => {
        if (!controller.signal.aborted) setHistory([]);
      });
    return () => controller.abort();
  }, [current.id, version]);

  const adopt = (a: Appointment, message?: string) => {
    setCurrent(a);
    setPlace(placementOf(a));
    setNote(a.notes ?? "");
    setMode("details");
    setLines([]);
    setConflict(null);
    setSaved(message ?? null);
    onChanged(a);
    // the button that was just used is gone or disabled, so keyboard focus would be lost: put it back on the appointment's heading
    queueMicrotask(() => document.getElementById("cal-drawer-title")?.focus());
  };

  async function run(action: () => Promise<Appointment>, message: string) {
    if (busy) return;
    setBusy(true);
    setLines([]);
    setSaved(null);
    try {
      adopt(await action(), message);
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else if (err instanceof ApiError) {
        setLines(await explainRefusal(err, {
          provider: snapshot.providers.find((p) => p.providerId === place.providerId)?.displayName,
          operatory: snapshot.operatories.find((o) => o.id === place.operatoryId)?.name,
        }));
      } else setLines(["We could not confirm whether that was saved - the connection dropped. Reload to see the appointment as it is now, then try again."]);
    } finally {
      setBusy(false);
    }
  }

  async function reload() {
    try {
      adopt(await getAppointment(current.id));
    } catch {
      setLines(["Could not reload the appointment."]);
    }
  }

  const submitReschedule = (event: FormEvent) => {
    event.preventDefault();
    if (!place.providerId || !place.operatoryId || !place.date || !place.time) { setLines(["Choose the provider, operatory, date and start time."]); return; }
    void run(() => rescheduleAppointment(current.id, {
      providerId: place.providerId, operatoryId: place.operatoryId, startLocal: `${place.date}T${place.time}`,
      durationMinutes: place.duration.trim() === "" ? null : Number(place.duration),
    }, version), "Appointment rescheduled.");
  };
  const submitCancel = (event: FormEvent) => {
    event.preventDefault();
    void run(() => cancelAppointment(current.id, reason, version), "Appointment cancelled.");
  };
  const submitNote = (event: FormEvent) => {
    event.preventDefault();
    void run(() => updateAppointmentNotes(current.id, note, version), "Note saved.");
  };

  const scheduled = current.status === "Scheduled";
  const flow = current.flowState ?? "Scheduled";
  const arrived = flow !== "Scheduled" && flow !== "Confirmed"; // Confirmed has not arrived yet
  const onBoard = flow === "Ready" || flow === "Seated" || flow === "CheckedOut";
  const flowLabel = flowWord(current);
  const started = nowLocal >= current.startLocal;
  const noteDirty = note.trim() !== (current.notes ?? "").trim();

  return (
    <div className="cal-panel">
      <h2 className="alv-workspace__section-title" id="cal-drawer-title" tabIndex={-1}>{current.patientName}</h2>
      <p className="cal-panel__status">
        <span className={`cal-badge cal-badge--${current.status.toLowerCase()}`}>{statusWord(current.status)}</span>
        {flowLabel && <span className={`cal-badge cal-badge--flow-${flow.toLowerCase()}`} data-testid="flow-badge">{flowLabel}</span>}
      </p>
      <dl className="alv-form-answers">
        <div className="alv-form-answers__row"><dt>When</dt><dd>{dateOf(current.startLocal)} {timeOf(current.startLocal)}–{timeOf(current.endLocal)} ({current.durationMinutes} min)</dd></div>
        <div className="alv-form-answers__row"><dt>Provider</dt><dd>{current.providerName}</dd></div>
        <div className="alv-form-answers__row"><dt>Operatory</dt><dd>{current.operatoryName}</dd></div>
        {(current.visitProviderId !== undefined && current.visitProviderId !== current.providerId) || (current.visitOperatoryId !== undefined && current.visitOperatoryId !== current.operatoryId) ? (
          <div className="alv-form-answers__row"><dt>Seen by, in</dt><dd>{current.visitProviderName}, {current.visitOperatoryName}</dd></div>
        ) : null}
        <div className="alv-form-answers__row"><dt>Type</dt><dd>{current.appointmentTypeName}</dd></div>
        {flowLabel && current.flowChangedAtUtc && <div className="alv-form-answers__row"><dt>{flowLabel} recorded</dt><dd>{new Date(current.flowChangedAtUtc).toLocaleString()}</dd></div>}
        {current.status === "Cancelled" && <div className="alv-form-answers__row"><dt>Reason cancelled</dt><dd>{current.cancelReason}</dd></div>}
        {current.statusChangedAtUtc && current.status !== "Scheduled" && <div className="alv-form-answers__row"><dt>{statusWord(current.status)} recorded</dt><dd>{new Date(current.statusChangedAtUtc).toLocaleString()}</dd></div>}
      </dl>

      <p className={saved ? "alv-workspace__saved" : "alv-workspace__saved-slot"} role="status">{saved}</p>
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => void reload()} />}
      {lines.length > 0 && <div className="alv-schedule__refusal" role="alert">{lines.map((l) => <p key={l}>{l}</p>)}</div>}

      {mode === "details" && canManage && scheduled && flow !== "Completed" && (
        <div className="cal-panel__actions" role="group" aria-label="Patient flow">
          {(flow === "Scheduled" || flow === "Confirmed") && <Button variant="primary" onClick={() => void run(() => checkInPatient(current.id, version), "Patient checked in.")} disabled={busy}>Check in</Button>}
          {flow === "CheckedIn" && <Button variant="primary" onClick={() => void run(() => startTreatment(current.id, version), "Treatment started.")} disabled={busy}>Start treatment</Button>}
          {(flow === "CheckedIn" || flow === "InTreatment") && <Button variant={flow === "InTreatment" ? "primary" : undefined} onClick={() => void run(() => completeTreatment(current.id, version), "Treatment completed.")} disabled={busy}>Complete treatment</Button>}
        </div>
      )}

      {mode === "details" && scheduled && onBoard && (
        <p className="alv-workspace__note">This visit is under way. Continue it on the <Link to="/flow">Visit board</Link>.</p>
      )}

      {mode === "details" && canManage && scheduled && !arrived && (
        <div className="cal-panel__actions">
          <Button onClick={() => { setMode("reschedule"); setLines([]); setSaved(null); }} disabled={busy}>Reschedule</Button>
          <Button variant="danger" onClick={() => { setMode("cancel"); setLines([]); setSaved(null); }} disabled={busy}>Cancel appointment</Button>
          <Button onClick={() => void run(() => markNoShow(current.id, version), "Marked as a no-show.")} disabled={busy || !started}
            aria-describedby={!started ? "noshow-hint" : undefined}>Mark no-show</Button>
        </div>
      )}
      {mode === "details" && canManage && scheduled && !arrived && !started && <p id="noshow-hint" className="alv-workspace__note">A no-show can be recorded once the start time has passed.</p>}

      {mode === "reschedule" && (
        <form className="alv-workspace__form" onSubmit={submitReschedule} noValidate aria-label="Reschedule appointment">
          <h3 className="alv-workspace__subtitle">Move to</h3>
          <PlacementFields snapshot={snapshot} values={place} onChange={setPlace} disabled={busy} durationHint={`Leave blank to keep ${current.durationMinutes} minutes.`} />
          <div className="cal-panel__actions">
            <Button type="submit" variant="primary" disabled={busy}>{busy ? "Saving…" : "Save new time"}</Button>
            <Button type="button" onClick={() => { setMode("details"); setLines([]); setPlace(placementOf(current)); }} disabled={busy}>Keep as it is</Button>
          </div>
        </form>
      )}

      {mode === "cancel" && (
        <form className="alv-workspace__form" onSubmit={submitCancel} noValidate aria-label="Cancel appointment">
          <div className="alv-form-field">
            <label htmlFor={reasonId} className="alv-form-field__label">Reason for cancelling (required)</label>
            <textarea id={reasonId} className="alv-form-field__input alv-form-inputs__textarea" rows={3} maxLength={400} value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} />
          </div>
          <p className="alv-workspace__note">The appointment stays on the calendar, marked as cancelled, and stops holding the time.</p>
          <div className="cal-panel__actions">
            <Button type="submit" variant="danger" disabled={busy}>{busy ? "Cancelling…" : "Confirm cancellation"}</Button>
            <Button type="button" onClick={() => { setMode("details"); setLines([]); }} disabled={busy}>Keep appointment</Button>
          </div>
        </form>
      )}

      <form className="alv-workspace__form" onSubmit={submitNote} noValidate aria-label="Appointment note">
        <div className="alv-form-field">
          <label htmlFor={noteId} className="alv-form-field__label">Note</label>
          <textarea id={noteId} className="alv-form-field__input alv-form-inputs__textarea" rows={2} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} disabled={busy || !canManage} />
        </div>
        {canManage && <div><Button type="submit" disabled={busy || !noteDirty}>Save note</Button></div>}
      </form>

      <section aria-labelledby="cal-history-title">
        <h3 id="cal-history-title" className="alv-workspace__subtitle">History</h3>
        {history === null ? <p className="alv-workspace__note">Loading history…</p> : (
          <table className="alv-workspace__table">
            <caption className="alv-workspace__caption">What happened to this appointment, oldest first</caption>
            <thead><tr><th scope="col">When</th><th scope="col">What</th><th scope="col">Details</th></tr></thead>
            <tbody>
              {history.map((e, i) => (
                <tr key={`${e.eventType}-${i}`}>
                  <td>{new Date(e.occurredAtUtc).toLocaleString()}</td>
                  <td>{EVENT_LABELS[e.eventType] ?? e.eventType}</td>
                  <td>
                    {e.eventType === "Rescheduled" && e.previousStartLocal
                      ? `Was ${dateOf(e.previousStartLocal)} ${timeOf(e.previousStartLocal)} with ${e.previousProviderName ?? "?"} in ${e.previousOperatoryName ?? "?"}`
                      : e.detail ?? "—"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}

function placementOf(a: Appointment): Placement {
  return { providerId: a.providerId, operatoryId: a.operatoryId, date: dateOf(a.startLocal), time: timeOf(a.startLocal), duration: "" };
}
