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

interface Props {
  snapshot: SchedulingSnapshot;
  initial: { providerId?: string; date: string; time: string };
  onBooked: (appointment: Appointment) => void;
}

/**
 * ALV-004-C01: the "new appointment" drawer. Fast patient lookup (the shared picker), an appointment type whose default duration is shown and used when
 * the duration is left blank, and the same retry-safe booking STORY-004 introduced: one idempotency key per distinct request, kept across a dropped
 * connection so Book again cannot book twice. A refusal is explained in words inside the drawer (what is in the way, and when) and nothing is booked.
 */
export function NewAppointmentPanel({ snapshot, initial, onBooked }: Props) {
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [typeId, setTypeId] = useState("");
  const [place, setPlace] = useState<Placement>({ providerId: initial.providerId ?? "", operatoryId: "", date: initial.date, time: initial.time, duration: "" });
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
    if (!patient || !typeId || !place.providerId || !place.operatoryId || !place.date || !place.time) {
      setUnconfirmed(false);
      setLines(["Choose the patient, appointment type, provider, operatory, date and time first."]);
      return;
    }
    const input = {
      patientId: patient.id, providerId: place.providerId, operatoryId: place.operatoryId, appointmentTypeId: typeId, startLocal: `${place.date}T${place.time}`,
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
        }));
      } else setUnconfirmed(true); // no response at all: it may or may not be booked; the same key is kept
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-workspace__form" onSubmit={submit} noValidate aria-label="New appointment">
      <PatientPicker key="picker" label="Patient" selected={patient} onSelect={(p) => { setPatient(p); setLines([]); }} disabled={busy} />
      <div className="alv-form-field">
        <label htmlFor={typeSelect} className="alv-form-field__label">Appointment type *</label>
        <select id={typeSelect} className="alv-form-field__input" value={typeId} onChange={(e) => setTypeId(e.target.value)} disabled={busy}>
          <option value="">Choose…</option>
          {snapshot.appointmentTypes.map((t) => <option key={t.id} value={t.id}>{t.name} ({t.defaultDurationMinutes} min)</option>)}
        </select>
      </div>
      <PlacementFields snapshot={snapshot} values={place} onChange={setPlace} disabled={busy}
        durationHint={type ? `Leave blank to use ${type.defaultDurationMinutes} minutes.` : "Leave blank to use the appointment type's default."} />
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
