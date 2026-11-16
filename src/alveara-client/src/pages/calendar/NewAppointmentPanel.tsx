import { useId, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { PatientPicker } from "../../components/PatientPicker";
import type { SchedulingSnapshot } from "../../services/configApi";
import { ApiError } from "../../services/authApi";
import type { PatientSummary } from "../../services/patientsApi";
import { newScheduleKey, scheduleAppointment } from "../../services/schedulingApi";
import type { Appointment } from "../../services/schedulingApi";
import { explainRefusal } from "./explainRefusal";
import { PlacementFields } from "./PlacementFields";
import type { Placement } from "./PlacementFields";
import { bookingDurationHint, durationAfterTypeChange } from "./bookingDuration";

/** ALV-N021 A2: who and what a Rebook starts from (the cancelled or no-show appointment). */
export interface Rebook { patientId: string; patientName: string; appointmentTypeId: string; operatoryId: string }

interface Props {
  snapshot: SchedulingSnapshot;
  initial: { providerId?: string; date: string; time: string; rebook?: Rebook };
  onBooked: (appointment: Appointment) => void;
}

/**
 * ALV-004-C01: the "new appointment" drawer. Fast patient lookup (the shared picker), an appointment type whose default duration is shown and used when
 * the duration is left blank, and the same retry-safe booking STORY-004 introduced: one idempotency key per distinct request, kept across a dropped
 * connection so Book again cannot book twice. A refusal is explained in words inside the drawer (what is in the way, and when) and nothing is booked.
 */
export function NewAppointmentPanel({ snapshot, initial, onBooked }: Props) {
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  // ALV-N021 A2 (F-045, F-051, F-073): rebooking after a cancellation or a no-show starts with the same patient and appointment type; both can still be changed
  const [rebookPatient, setRebookPatient] = useState<Rebook | null>(initial.rebook ?? null);
  const [typeId, setTypeId] = useState(initial.rebook?.appointmentTypeId ?? "");
  const rebookLength = snapshot.appointmentTypes.find((t) => t.id === initial.rebook?.appointmentTypeId)?.defaultDurationMinutes;
  const [place, setPlace] = useState<Placement>({ providerId: initial.providerId ?? "", operatoryId: initial.rebook?.operatoryId ?? "", date: initial.date, time: initial.time,
    duration: rebookLength === undefined ? "" : String(rebookLength) });
  // ALV-N021 A2 (OD-21): whether the person typed the duration themselves (then a type change never replaces it)
  const durationTyped = useRef(false);
  const chooseType = (id: string) => {
    setTypeId(id);
    setPlace((p) => ({ ...p, duration: durationAfterTypeChange(p.duration, durationTyped.current, snapshot.appointmentTypes.find((t) => t.id === id)?.defaultDurationMinutes) }));
  };
  const changePlace = (next: Placement) => {
    if (next.duration !== place.duration) durationTyped.current = next.duration.trim() !== "";
    setPlace(next);
  };
  const [notes, setNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const [lines, setLines] = useState<string[]>([]);
  const [unconfirmed, setUnconfirmed] = useState(false);
  const attempt = useRef<{ signature: string; key: string } | null>(null);
  const typeSelect = useId();
  const notesId = useId();
  const type = snapshot.appointmentTypes.find((t) => t.id === typeId);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    const patientId = patient?.id ?? rebookPatient?.patientId;
    if (!patientId || !typeId || !place.providerId || !place.operatoryId || !place.date || !place.time) {
      setUnconfirmed(false);
      setLines(["Choose the patient, appointment type, provider, operatory, date and time first."]);
      return;
    }
    const input = {
      patientId, providerId: place.providerId, operatoryId: place.operatoryId, appointmentTypeId: typeId, startLocal: `${place.date}T${place.time}`,
      durationMinutes: place.duration.trim() === "" ? null : Number(place.duration), notes: notes.trim() === "" ? null : notes,
    };
    const signature = JSON.stringify(input);
    if (attempt.current?.signature !== signature) attempt.current = { signature, key: newScheduleKey() };
    setBusy(true);
    setLines([]);
    setUnconfirmed(false);
    try {
      onBooked(await scheduleAppointment(input, attempt.current.key));
    } catch (err) {
      if (err instanceof ApiError) {
        setLines(await explainRefusal(err, {
          provider: snapshot.providers.find((p) => p.providerId === place.providerId)?.displayName,
          operatory: snapshot.operatories.find((o) => o.id === place.operatoryId)?.name,
        }, "book"));
      } else setUnconfirmed(true); // no response at all: it may or may not be booked; the same key is kept
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-workspace__form" onSubmit={submit} noValidate aria-label="New appointment">
      {rebookPatient ? (
        <div className="alv-patient-picker">
          <p className="alv-patient-picker__selected" data-testid="rebook-patient"><span className="alv-patient-picker__label">Patient:</span> {rebookPatient.patientName} (rebooking)</p>
          <Button type="button" onClick={() => { setRebookPatient(null); setLines([]); }} disabled={busy}>Choose a different patient</Button>
        </div>
      ) : <PatientPicker key="picker" label="Patient" selected={patient} onSelect={(p) => { setPatient(p); setLines([]); }} disabled={busy} />}
      <div className="alv-form-field">
        <label htmlFor={typeSelect} className="alv-form-field__label">Appointment type *</label>
        <select id={typeSelect} className="alv-form-field__input" value={typeId} onChange={(e) => chooseType(e.target.value)} disabled={busy}>
          <option value="">Choose…</option>
          {snapshot.appointmentTypes.map((t) => <option key={t.id} value={t.id}>{t.name} ({t.defaultDurationMinutes} min)</option>)}
        </select>
      </div>
      <PlacementFields snapshot={snapshot} values={place} onChange={changePlace} disabled={busy}
        durationHint={bookingDurationHint(type?.defaultDurationMinutes)} />
      <div className="alv-form-field">
        <label htmlFor={notesId} className="alv-form-field__label">Note (optional)</label>
        <textarea id={notesId} className="alv-form-field__input alv-form-inputs__textarea" rows={2} maxLength={1000} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={busy} />
      </div>
      {lines.length > 0 && (
        <div className="alv-schedule__refusal" role="alert">{lines.map((l) => <p key={l}>{l}</p>)}</div>
      )}
      {unconfirmed && (
        <div className="alv-schedule__refusal" role="alert">
          <p>We could not confirm whether the appointment was booked - the connection dropped before an answer came back.</p>
          <p>You can press Book again: the same request is recognised, so it cannot be booked twice. The calendar shows what is actually booked.</p>
        </div>
      )}
      <div>
        <Button type="submit" variant="primary" disabled={busy}>{busy ? "Booking…" : unconfirmed ? "Book again" : "Book appointment"}</Button>
      </div>
    </form>
  );
}
