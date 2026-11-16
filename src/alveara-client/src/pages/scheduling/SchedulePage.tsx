import { useEffect, useId, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { PageHeader } from "../../components/PageHeader";
import { PatientPicker } from "../../components/PatientPicker";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError } from "../../services/authApi";
import { getSchedulingSnapshot } from "../../services/configApi";
import type { SchedulingSnapshot } from "../../services/configApi";
import type { PatientSummary } from "../../services/patientsApi";
import {
  dateOf, listAppointments, newScheduleKey, scheduleAppointment, timeOf,
} from "../../services/schedulingApi";
import type { Appointment } from "../../services/schedulingApi";
import { explainRefusal } from "../calendar/explainRefusal";
import { TIME_HINT } from "../calendar/PlacementFields";
import { bookingDurationHint, durationAfterTypeChange } from "../calendar/bookingDuration";
import { flowWord, statusWord } from "../calendar/calendarLayout";
import { formFieldMessage } from "./scheduleMessages";
import "./SchedulePage.css";
import { sessionToday, addDaysToDate } from "../../time/practiceTime";

type Load = { kind: "loading" } | { kind: "error" } | { kind: "loaded"; options: SchedulingSnapshot };
type Outcome =
  | { kind: "none" }
  | { kind: "confirmed"; appointment: Appointment }
  | { kind: "refused"; lines: string[] }
  | { kind: "unconfirmed" }
  | { kind: "error"; message: string };

// ALV-N021 A3: the practice's tomorrow (date-only arithmetic on the practice day); empty without a practice zone
const tomorrow = () => {
  const today = sessionToday();
  return today === null ? "" : addDaysToDate(today, 1);
};

/**
 * STORY-004: schedule an appointment without conflicts, and see that day's schedule. The server decides every conflict (provider
 * double-booked, operatory in use, provider unavailable, invalid duration); this page only asks and explains the answer in plain words,
 * naming the appointment already holding the time. Each booking attempt carries one Idempotency-Key that is kept while the request is
 * unchanged, so pressing Book again after a dropped connection can never book twice. This is a booking form and a day list, not the
 * calendar (a later story).
 */
export function SchedulePage() {
  const { hasPermission } = useAuth();
  const canBook = hasPermission("ManageAppointments");
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [providerId, setProviderId] = useState("");
  const [operatoryId, setOperatoryId] = useState("");
  const [typeId, setTypeId] = useState("");
  const [date, setDate] = useState(tomorrow);
  const [time, setTime] = useState("09:00");
  const [duration, setDuration] = useState("");
  const durationTyped = useRef(false);   // ALV-N021 A2 (OD-21): a typed duration is never replaced by a type change
  const [busy, setBusy] = useState(false);
  const [outcome, setOutcome] = useState<Outcome>({ kind: "none" });
  const [day, setDay] = useState<Appointment[] | null>(null);
  const [dayFailed, setDayFailed] = useState(false);
  const [filterProvider, setFilterProvider] = useState("");
  const [refreshKey, setRefreshKey] = useState(0);
  const [pickerKey, setPickerKey] = useState(0); // remounting the picker clears its search text and results for the next booking
  const attempt = useRef<{ signature: string; key: string } | null>(null);
  const ids = { provider: useId(), operatory: useId(), type: useId(), filter: useId() };

  useEffect(() => {
    const controller = new AbortController();
    getSchedulingSnapshot()
      .then((options) => {
        if (!controller.signal.aborted) setLoad({ kind: "loaded", options });
      })
      .catch(() => {
        if (!controller.signal.aborted) setLoad({ kind: "error" });
      });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) return;
    const controller = new AbortController();
    listAppointments(date, date, filterProvider || undefined, controller.signal)
      .then((rows) => {
        setDayFailed(false);
        setDay(rows);
      })
      .catch(() => {
        if (!controller.signal.aborted) setDayFailed(true);
      });
    return () => controller.abort();
  }, [date, filterProvider, refreshKey]);

  useUnsavedChangesWarning(patient !== null && outcome.kind !== "confirmed");
  // once a booking is confirmed the form is "done" (Book is disabled, so a quick second click cannot replace the confirmation with an error)
  // until the user changes something for the next booking
  const touch = () => setOutcome((o) => (o.kind === "confirmed" ? { kind: "none" } : o));

  if (load.kind === "loading") return <LoadingState label="Loading scheduling options…" />;
  if (load.kind === "error") return <ErrorState title="Could not load the scheduling options" description="Check your connection and reload the page." />;
  const { options } = load;
  const type = options.appointmentTypes.find((t) => t.id === typeId);
  const provider = options.providers.find((p) => p.providerId === providerId);
  const operatory = options.operatories.find((o) => o.id === operatoryId);

  // ALV-N021 A2 (F-072): a conflict is explained in the one shared format (what is in the way and when, then "Nothing was booked."), the same-patient conflict included;
  // a field problem keeps its field-level words
  async function explain(err: unknown): Promise<string[]> {
    if (!(err instanceof ApiError)) return [];
    if (CONFLICTS.has(err.code)) return explainRefusal(err, { provider: provider?.displayName, operatory: operatory?.name }, "book");
    return [formFieldMessage(err)];
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy || !canBook) return;
    if (!patient || !providerId || !operatoryId || !typeId || !date || !time) {
      setOutcome({ kind: "refused", lines: ["Choose the patient, provider, operatory, appointment type, date and time first."] });
      return;
    }
    const input = {
      patientId: patient.id, providerId, operatoryId, appointmentTypeId: typeId, startLocal: `${date}T${time}`,
      durationMinutes: duration.trim() === "" ? null : Number(duration),
    };
    // one key per distinct request: unchanged inputs after a dropped connection reuse it, a changed request gets a new one
    const signature = JSON.stringify(input);
    if (attempt.current?.signature !== signature) attempt.current = { signature, key: newScheduleKey() };
    setBusy(true);
    setOutcome({ kind: "none" });
    try {
      const appointment = await scheduleAppointment(input, attempt.current.key);
      setOutcome({ kind: "confirmed", appointment });
      setPatient(null);
      setPickerKey((k) => k + 1);
      attempt.current = null;
      setRefreshKey((k) => k + 1);
    } catch (err) {
      if (err instanceof ApiError) setOutcome({ kind: "refused", lines: await explain(err) });
      else setOutcome({ kind: "unconfirmed" }); // no response at all: it may or may not have been booked; the same key is kept
      setRefreshKey((k) => k + 1);
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <PageHeader title="Schedule appointment" description="Book a patient with a provider in an operatory. The system refuses any time the provider or operatory is already taken." />
      {canBook ? (
        <form className="alv-schedule__form" onSubmit={submit} noValidate aria-label="Schedule an appointment">
          <PatientPicker key={pickerKey} label="Patient" selected={patient} onSelect={(p) => { setPatient(p); setOutcome({ kind: "none" }); }} disabled={busy} />
          <div className="alv-schedule__grid">
            <SelectField id={ids.provider} label="Provider *" value={providerId} onChange={(v) => { touch(); setProviderId(v); }} disabled={busy}
              options={options.providers.map((p) => ({ value: p.providerId, label: p.displayName }))} />
            <SelectField id={ids.operatory} label="Operatory *" value={operatoryId} onChange={(v) => { touch(); setOperatoryId(v); }} disabled={busy}
              options={options.operatories.map((o) => ({ value: o.id, label: o.name }))} />
            <SelectField id={ids.type} label="Appointment type *" value={typeId} onChange={(v) => { touch(); setTypeId(v); setDuration((d) => durationAfterTypeChange(d, durationTyped.current, options.appointmentTypes.find((t) => t.id === v)?.defaultDurationMinutes)); }} disabled={busy}
              options={options.appointmentTypes.map((t) => ({ value: t.id, label: `${t.name} (${t.defaultDurationMinutes} min)` }))} />
            <FormField label="Date *" type="date" value={date} onChange={(e) => { touch(); setDate(e.target.value); }} disabled={busy} />
            <FormField label="Start time *" type="time" step={300} value={time} onChange={(e) => { touch(); setTime(e.target.value); }} disabled={busy} hint={TIME_HINT} />
            <FormField label="Duration in minutes (optional)" type="number" min={5} max={480} step={5} value={duration} onChange={(e) => { touch(); durationTyped.current = e.target.value.trim() !== ""; setDuration(e.target.value); }} disabled={busy}
              hint={bookingDurationHint(type?.defaultDurationMinutes)} />
          </div>

          <div role="status" className={outcome.kind === "confirmed" ? "alv-schedule__result" : "alv-schedule__result-slot"}>
            {outcome.kind === "confirmed" && (
              <>
                <strong>Appointment booked.</strong> {outcome.appointment.patientName} with {outcome.appointment.providerName} in {outcome.appointment.operatoryName},{" "}
                {dateOf(outcome.appointment.startLocal)} {timeOf(outcome.appointment.startLocal)}–{timeOf(outcome.appointment.endLocal)} ({outcome.appointment.appointmentTypeName}).
              </>
            )}
          </div>
          {outcome.kind === "refused" && (
            <div className="alv-schedule__refusal" role="alert">
              {outcome.lines.map((l) => <p key={l}>{l}</p>)}
            </div>
          )}
          {outcome.kind === "unconfirmed" && (
            <div className="alv-schedule__refusal" role="alert">
              <p>We could not confirm whether the appointment was booked - the connection dropped before an answer came back.</p>
              <p>You can press Book again: the same request is recognised, so it cannot be booked twice. The schedule below shows what is actually booked.</p>
            </div>
          )}
          <div>
            <Button type="submit" variant="primary" disabled={busy || outcome.kind === "confirmed"}>{busy ? "Booking…" : outcome.kind === "unconfirmed" ? "Book again" : "Book appointment"}</Button>
          </div>
        </form>
      ) : (
        <p className="alv-workspace__note">Your role can view the schedule but not book appointments.</p>
      )}

      <section className="alv-schedule__day" aria-labelledby="alv-day-title">
        <h2 id="alv-day-title" className="alv-workspace__section-title">Appointments on {date}</h2>
        <div className="alv-schedule__day-controls">
          <FormField label="Day" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          <SelectField id={ids.filter} label="Provider" value={filterProvider} onChange={setFilterProvider} blank="All providers"
            options={options.providers.map((p) => ({ value: p.providerId, label: p.displayName }))} />
        </div>
        {dayFailed ? (
          <ErrorState title="Could not load the schedule" />
        ) : day === null ? (
          <LoadingState label="Loading the schedule…" />
        ) : day.length === 0 ? (
          <EmptyState title="Nothing booked" description="No appointments on this day for this selection." />
        ) : (
          <table className="alv-workspace__table">
            <caption className="alv-workspace__caption">Booked appointments, earliest first</caption>
            <thead>
              <tr><th scope="col">Time</th><th scope="col">Patient</th><th scope="col">Status</th><th scope="col">Provider</th><th scope="col">Operatory</th><th scope="col">Type</th></tr>
            </thead>
            <tbody>
              {day.map((a) => (
                <tr key={a.id}>
                  {/* ALV-N021 A2 (F-069): each row opens the appointment in the calendar and the patient's record, and says where it stands */}
                  <td><Link to={`/calendar?day=${dateOf(a.startLocal)}&appointment=${a.id}`} aria-label={`${timeOf(a.startLocal)}–${timeOf(a.endLocal)}: open the appointment of ${a.patientName} in the calendar`}>{timeOf(a.startLocal)}–{timeOf(a.endLocal)}</Link></td>
                  <td><Link to={`/patients/${a.patientId}`}>{a.patientName}</Link></td>
                  <td>{flowWord(a) ?? statusWord(a.status)}</td>
                  <td>{a.providerName}</td>
                  <td>{a.operatoryName}</td>
                  <td>{a.appointmentTypeName} ({a.durationMinutes} min)</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </>
  );
}

/** The refusals that name what is in the way; everything else is a field problem. */
const CONFLICTS = new Set(["provider_double_booked", "operatory_conflict", "patient_double_booked", "provider_unavailable", "schedule_busy"]);

function SelectField({ id, label, value, onChange, options, disabled, blank = "Choose…" }: {
  id: string; label: string; value: string; onChange: (v: string) => void; options: { value: string; label: string }[]; disabled?: boolean; blank?: string;
}) {
  return (
    <div className="alv-form-field">
      <label htmlFor={id} className="alv-form-field__label">{label}</label>
      <select id={id} className="alv-form-field__input" value={value} onChange={(e) => onChange(e.target.value)} disabled={disabled}>
        <option value="">{blank}</option>
        {options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
    </div>
  );
}
