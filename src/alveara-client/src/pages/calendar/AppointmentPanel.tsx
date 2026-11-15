import { useEffect, useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import type { SchedulingSnapshot } from "../../services/configApi";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import {
  cancelAppointment, cancelCategoryLabel, dateOf, getAppointment, getAppointmentHistory, markNoShow, rescheduleAppointment, timeOf, updateAppointmentNotes,
} from "../../services/schedulingApi";
import type { Appointment, AppointmentEvent } from "../../services/schedulingApi";
import { assignVisit, getAppointmentActions, keepDespiteBlock, moveVisit, undoCheckIn } from "../../services/visitsApi";
import type { AppointmentActions, VisitAction } from "../../services/visitsApi";
import { AssignPanel } from "../flow/AssignPanel";
import { CheckoutPanel } from "../flow/CheckoutPanel";
import { ReasonForm } from "../flow/ReasonForm";
import { flowWord, statusWord } from "./calendarLayout";
import { CancelForm } from "./CancelForm";
import { explainRefusal } from "./explainRefusal";
import { PlacementFields } from "./PlacementFields";
import type { Placement } from "./PlacementFields";

interface Props {
  appointment: Appointment;
  snapshot: SchedulingSnapshot;
  /** May edit the appointment's note (the only change not decided by the server's actions list). */
  canManage: boolean;
  /** Kept for the page's interface; the no-show rule now comes with the server's actions. */
  nowLocal?: string;
  onChanged: (appointment: Appointment) => void;
}

type Mode = "details" | "reschedule" | "cancel" | "undo" | "keep" | "assign" | "complete";

const EVENT_LABELS: Record<string, string> = { Scheduled: "Scheduled", Rescheduled: "Rescheduled", Cancelled: "Cancelled", NoShow: "Marked as a no-show", NotesChanged: "Note changed",
  CheckedIn: "Patient checked in", TreatmentStarted: "Treatment started", Completed: "Visit completed",
  Confirmed: "Confirmed", Ready: "Marked ready", Seated: "Seated", CheckedOut: "Ready for checkout", AssignmentChanged: "Room or provider changed (earlier rule)",
  CheckInUndone: "Check-in undone", ProviderReassigned: "Provider changed", RoomChanged: "Room changed", KeptDespiteBlock: "Kept despite blocked time",
};

/** Visit moves and visit-level changes (the "Patient flow" group); booking changes are a separate group. */
const FLOW_CODES = new Set(["confirm", "check_in", "mark_ready", "seat", "start_treatment", "end_treatment", "complete", "undo_check_in", "keep_despite_block"]);
const DONE: Record<string, string> = {
  confirm: "Confirmed.", check_in: "Patient checked in.", mark_ready: "Marked ready.", seat: "Patient seated.", start_treatment: "Treatment started.",
  end_treatment: "Treatment ended; ready for checkout.", complete: "Visit completed.",
};

/**
 * ALV-004-C01: one appointment in the drawer - details, history and the changes that can be made to it, each carrying the version this panel loaded (a change
 * someone else made first is refused and reported, never applied on top). Cancelled and no-show appointments stay readable with their category, who/when and history.
 *
 * ALV-N021 A2: every button comes from the server's list of actions for the signed-in user (GET appointments/{id}/actions) - the same list the Visit board uses -
 * so the calendar and the board can never offer different things. A blocked action stays visible but disabled with the server's reason. Moves go through the one
 * visit path (no STORY-011 shortcuts); cancel asks for a category; provider/room changes move the booking. The panel says who the visit waits for.
 */
export function AppointmentPanel({ appointment, snapshot, canManage, onChanged }: Props) {
  const [current, setCurrent] = useState(appointment);
  const [actions, setActions] = useState<AppointmentActions | null>(null);
  const [actionsFailed, setActionsFailed] = useState(false);
  const [mode, setMode] = useState<Mode>("details");
  const [place, setPlace] = useState<Placement>(() => placementOf(appointment));
  const [note, setNote] = useState(appointment.notes ?? "");
  const [history, setHistory] = useState<AppointmentEvent[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [lines, setLines] = useState<string[]>([]);
  const [saved, setSaved] = useState<string | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const noteId = useId();

  const version = current.rowVersion ?? "";
  useEffect(() => {
    const controller = new AbortController();
    getAppointmentHistory(current.id, controller.signal).then(setHistory).catch(() => { if (!controller.signal.aborted) setHistory([]); });
    getAppointmentActions(current.id, controller.signal)
      .then((x) => { setActions(x); setActionsFailed(false); })
      .catch(() => { if (!controller.signal.aborted) { setActions(null); setActionsFailed(true); } });
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
      const a = await action();
      const next = await getAppointmentActions(a.id).catch(() => null);   // announce who the visit waits for now (the effect also refreshes the list)
      adopt(a, next?.nextActorText ? `${message} ${next.nextActorText}` : message);
    } catch (err) {
      // the open form (reschedule, cancel, a reason) stays open so the user can try again
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

  const press = (x: VisitAction) => {
    setLines([]); setSaved(null);
    switch (x.code) {
      case "reschedule": setMode("reschedule"); return;
      case "cancel": setMode("cancel"); return;
      case "undo_check_in": setMode("undo"); return;
      case "keep_despite_block": setMode("keep"); return;
      case "complete": setMode("complete"); return;
      case "reassign_provider": case "change_room": setMode("assign"); return;
      case "no_show": void run(() => markNoShow(current.id, version), "Marked as a no-show."); return;
      default: if (x.targetState) { const t = x.targetState; void run(() => moveVisit(current.id, t, version), DONE[x.code] ?? "Saved."); }
    }
  };

  const submitReschedule = (event: FormEvent) => {
    event.preventDefault();
    if (!place.providerId || !place.operatoryId || !place.date || !place.time) { setLines(["Choose the provider, operatory, date and start time."]); return; }
    void run(() => rescheduleAppointment(current.id, {
      providerId: place.providerId, operatoryId: place.operatoryId, startLocal: `${place.date}T${place.time}`,
      durationMinutes: place.duration.trim() === "" ? null : Number(place.duration),
    }, version), "Appointment rescheduled.");
  };
  const submitNote = (event: FormEvent) => {
    event.preventDefault();
    void run(() => updateAppointmentNotes(current.id, note, version), "Note saved.");
  };

  const list = actions?.actions ?? [];
  const flowActions = list.filter((x) => FLOW_CODES.has(x.code));
  const bookingActions = list.filter((x) => x.code === "reschedule" || x.code === "cancel" || x.code === "no_show");
  const allowProvider = list.some((x) => x.code === "reassign_provider");
  const allowRoom = list.some((x) => x.code === "change_room");
  const flow = current.flowState ?? "Scheduled";
  const flowLabel = flowWord(current);
  const noteDirty = note.trim() !== (current.notes ?? "").trim();
  const flagged = actions?.blockFlagged ?? current.blockFlagged ?? false;
  const back = () => { setMode("details"); setLines([]); setPlace(placementOf(current)); };

  const button = (x: VisitAction, primary: boolean) => {
    const hintId = `cal-${current.id}-${x.code}-blocked`;
    return (
      <span key={x.code} className="cal-panel__action">
        <Button variant={x.code === "cancel" ? "danger" : primary && x.enabled ? "primary" : undefined} disabled={busy || !x.enabled}
          aria-describedby={x.enabled ? undefined : hintId} onClick={() => press(x)}>{x.code === "cancel" ? "Cancel appointment" : x.label}</Button>
        {!x.enabled && x.blockedText && <span id={hintId} className="alv-workspace__note">{x.blockedText}</span>}
      </span>
    );
  };

  return (
    <div className="cal-panel">
      <h2 className="alv-workspace__section-title" id="cal-drawer-title" tabIndex={-1}>{current.patientName}</h2>
      <p className="cal-panel__status">
        <span className={`cal-badge cal-badge--${current.status.toLowerCase()}`}>{statusWord(current.status)}</span>
        {flowLabel && <span className={`cal-badge cal-badge--flow-${flow.toLowerCase()}`} data-testid="flow-badge">{flowLabel}</span>}
        {flagged && <span className="cal-badge cal-badge--blocked" data-testid="block-flag">Blocked time</span>}
      </p>
      <dl className="alv-form-answers">
        <div className="alv-form-answers__row"><dt>When</dt><dd>{dateOf(current.startLocal)} {timeOf(current.startLocal)}–{timeOf(current.endLocal)} ({current.durationMinutes} min)</dd></div>
        <div className="alv-form-answers__row"><dt>Provider</dt><dd>{current.providerName}</dd></div>
        <div className="alv-form-answers__row"><dt>Operatory</dt><dd>{current.operatoryName}</dd></div>
        <div className="alv-form-answers__row"><dt>Type</dt><dd>{current.appointmentTypeName}</dd></div>
        {flowLabel && current.flowChangedAtUtc && <div className="alv-form-answers__row"><dt>{flowLabel} recorded</dt><dd>{new Date(current.flowChangedAtUtc).toLocaleString()}</dd></div>}
        {current.status === "Cancelled" && (
          <>
            <div className="alv-form-answers__row"><dt>Why cancelled</dt><dd>{cancelCategoryLabel(current.cancelCategory)}{current.shortNotice ? " · short notice (under 24 hours)" : ""}</dd></div>
            {current.cancelReason && <div className="alv-form-answers__row"><dt>Note</dt><dd>{current.cancelReason}</dd></div>}
          </>
        )}
        {current.statusChangedAtUtc && current.status !== "Scheduled" && <div className="alv-form-answers__row"><dt>{statusWord(current.status)} recorded</dt><dd>{new Date(current.statusChangedAtUtc).toLocaleString()}</dd></div>}
      </dl>
      {current.status === "Scheduled" && actions?.nextActorText && <p className="cal-panel__next">{actions.nextActorText}</p>}

      <p className={saved ? "alv-workspace__saved" : "alv-workspace__saved-slot"} role="status">{saved}</p>
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => void reload()} />}
      {lines.length > 0 && <div className="alv-schedule__refusal" role="alert">{lines.map((l) => <p key={l}>{l}</p>)}</div>}
      {actionsFailed && <p className="alv-workspace__note" role="alert">Could not load what you can do with this appointment. <Button onClick={() => void reload()}>Reload</Button></p>}

      {mode === "details" && (flowActions.length > 0 || allowProvider || allowRoom) && (
        <div className="cal-panel__actions" role="group" aria-label="Patient flow">
          {flowActions.map((x, i) => button(x, i === 0))}
          {(allowProvider || allowRoom) && <Button onClick={() => setMode("assign")} disabled={busy}>{allowProvider ? "Change provider or room" : "Change room"}</Button>}
        </div>
      )}
      {mode === "details" && bookingActions.length > 0 && (
        <div className="cal-panel__actions" role="group" aria-label="Booking">{bookingActions.map((x) => button(x, false))}</div>
      )}

      {mode === "details" && current.status === "Scheduled" && flow === "CheckedOut" && (
        <CheckoutPanel appointmentId={current.id} patientName={current.patientName}
          onRecorded={() => void getAppointmentActions(current.id).then(setActions).catch(() => setActionsFailed(true))} />
      )}

      {mode === "complete" && (
        <div className="cal-panel__actions" role="group" aria-label={`Confirm: Complete visit for ${current.patientName}`}>
          <p>Complete the visit for {current.patientName}? This cannot be undone.</p>
          <Button variant="primary" disabled={busy} onClick={() => void run(() => moveVisit(current.id, "Completed", version), DONE.complete)}>Yes, complete the visit</Button>
          <Button disabled={busy} onClick={back}>Keep it open</Button>
        </div>
      )}
      {mode === "undo" && (
        <ReasonForm title={`Undo check-in for ${current.patientName}`} explanation="The patient goes back to the state before check-in. The check-in stays in the history."
          submitLabel="Undo check-in" busy={busy} onSubmit={(r) => void run(() => undoCheckIn(current.id, r, version), "Check-in undone.")} onCancel={back} />
      )}
      {mode === "keep" && (
        <ReasonForm title={`Keep the appointment for ${current.patientName}`} explanation="The appointment stays where it is despite the provider's blocked time, and can be checked in."
          submitLabel="Keep appointment" busy={busy} onSubmit={(r) => void run(() => keepDespiteBlock(current.id, r, version), "Appointment kept despite the blocked time.")} onCancel={back} />
      )}
      {mode === "assign" && (
        <AssignPanel snapshot={snapshot} providerId={current.providerId} operatoryId={current.operatoryId} patientName={current.patientName}
          allowProvider={allowProvider} allowRoom={allowRoom} busy={busy} onCancel={back}
          onSave={(p, o, r) => void run(() => assignVisit(current.id, p, o, version, r), p !== current.providerId ? "Provider changed." : "Room changed.")} />
      )}
      {mode === "reschedule" && (
        <form className="alv-workspace__form" onSubmit={submitReschedule} noValidate aria-label="Reschedule appointment">
          <h3 className="alv-workspace__subtitle">Move to</h3>
          <PlacementFields snapshot={snapshot} values={place} onChange={setPlace} disabled={busy} durationHint={`Leave blank to keep ${current.durationMinutes} minutes.`} />
          <div className="cal-panel__actions">
            <Button type="submit" variant="primary" disabled={busy}>{busy ? "Saving…" : "Save new time"}</Button>
            <Button type="button" onClick={back} disabled={busy}>Keep as it is</Button>
          </div>
        </form>
      )}
      {mode === "cancel" && <CancelForm busy={busy} onBack={back} onSubmit={(category, n) => void run(() => cancelAppointment(current.id, category, n, version), "Appointment cancelled.")} />}

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
                  <td>{describe(e)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}

/** One history row's details: where it was moved from, the cancel category, and the reason the person gave. */
function describe(e: AppointmentEvent): string {
  const parts: string[] = [];
  if ((e.eventType === "Rescheduled" || e.eventType === "ProviderReassigned" || e.eventType === "RoomChanged") && (e.previousStartLocal || e.previousProviderName || e.previousOperatoryName)) {
    const when = e.previousStartLocal ? `${dateOf(e.previousStartLocal)} ${timeOf(e.previousStartLocal)} ` : "";
    parts.push(`Was ${when}with ${e.previousProviderName ?? "?"} in ${e.previousOperatoryName ?? "?"}`);
  } else if (e.detail) parts.push(e.detail);
  if (e.category) parts.push(`Category: ${cancelCategoryLabel(e.category)}`);
  if (e.reason) parts.push(`Reason: ${e.reason}`);
  return parts.length > 0 ? parts.join(" · ") : "—";
}

function placementOf(a: Appointment): Placement {
  return { providerId: a.providerId, operatoryId: a.operatoryId, date: dateOf(a.startLocal), time: timeOf(a.startLocal), duration: "" };
}
