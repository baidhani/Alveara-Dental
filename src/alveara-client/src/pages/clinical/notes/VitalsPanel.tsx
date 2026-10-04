import { useId, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../../components/Button";
import { FormField } from "../../../components/FormField";
import { useUnsavedChangesWarning } from "../../../hooks/useUnsavedChangesWarning";
import { ApiError } from "../../../services/authApi";
import { isNetworkFailure, newKey } from "../../../services/clinicalApi";
import type { EncounterDetail, EncounterVitals } from "../../../services/clinicalApi";
import { emptyVitals, recordVitals, voidVitals } from "../../../services/clinicalNotesApi";
import type { VitalsInput } from "../../../services/clinicalNotesApi";
import type { SaveResult } from "../SectionPanel";
import "../Clinical.css";

const when = (iso: string) => new Date(iso).toLocaleString();

interface Props {
  encounter: EncounterDetail;
  editable: boolean;
  busy: boolean;
  /** Runs a change against the version on screen (the encounter view's own runner), so the status line and conflicts behave the same as for every other change. */
  run: (change: (rowVersion: string) => Promise<EncounterDetail>, savedText: string) => Promise<SaveResult>;
}

/** A reading as a clinician says it: "BP 120/80 mmHg, pulse 72 bpm, ...". Only what was measured is listed. */
function describeVitals(v: EncounterVitals): string {
  const parts = [
    v.systolicMmHg !== null && v.diastolicMmHg !== null && `BP ${v.systolicMmHg}/${v.diastolicMmHg} mmHg`,
    v.pulseBpm !== null && `Pulse ${v.pulseBpm} bpm`,
    v.respirationsPerMinute !== null && `Respirations ${v.respirationsPerMinute}/min`,
    v.temperatureC !== null && `Temperature ${v.temperatureC} °C`,
    v.oxygenSaturationPercent !== null && `SpO2 ${v.oxygenSaturationPercent}%`,
    v.weightKg !== null && `Weight ${v.weightKg} kg`,
    v.heightCm !== null && `Height ${v.heightCm} cm`,
  ].filter(Boolean);
  return parts.join(" · ");
}

const FIELDS: { key: keyof VitalsInput; label: string; hint?: string }[] = [
  { key: "systolicMmHg", label: "Systolic (mmHg)" },
  { key: "diastolicMmHg", label: "Diastolic (mmHg)" },
  { key: "pulseBpm", label: "Pulse (beats per minute)" },
  { key: "respirationsPerMinute", label: "Respirations (per minute)" },
  { key: "temperatureC", label: "Temperature (°C)", hint: "One decimal place at most." },
  { key: "oxygenSaturationPercent", label: "Oxygen saturation (%)" },
  { key: "weightKg", label: "Weight (kg)", hint: "One decimal place at most." },
  { key: "heightCm", label: "Height (cm)", hint: "One decimal place at most." },
];

/**
 * ALV-005-C01: the encounter's vital signs. Each reading is attributed to the encounter, the time it was measured and who recorded it. A reading is never edited: a mistake
 * is voided (it stays listed, marked, with who voided it and why) and the right reading recorded again. The form keeps everything typed until the server accepts it, and one
 * idempotency key per reading is reused on every retry, so a dropped connection can never record the same reading twice.
 */
export function VitalsPanel({ encounter, editable, busy, run }: Props) {
  const [values, setValues] = useState<VitalsInput>(emptyVitals());
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [notice, setNotice] = useState<string | null>(null);
  const [voiding, setVoiding] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  const [reasonError, setReasonError] = useState<string | null>(null);
  const key = useRef<string | null>(null);
  const noteId = useId();
  const atId = useId();
  const reasonId = useId();
  const dirty = (Object.keys(values) as (keyof VitalsInput)[]).some((k) => values[k] !== "");
  useUnsavedChangesWarning(editable && dirty);
  const set = (patch: Partial<VitalsInput>) => setValues((v) => ({ ...v, ...patch }));

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setErrors({});
    setNotice(null);
    key.current ??= newKey(); // one key for this reading, reused by every retry of it
    const sent = key.current;
    let failure: unknown = null;
    const refused = await run(async (rowVersion) => {
      try {
        return await recordVitals(encounter.id, values, rowVersion, sent);
      } catch (err) {
        failure = err;
        throw err;
      }
    }, "Vital signs saved.");
    if (refused === null) {
      key.current = null;
      setValues(emptyVitals());
      return;
    }
    setErrors(refused);
    if (failure instanceof ApiError) key.current = null; // a definite refusal: the next attempt is a new reading
    else if (isNetworkFailure(failure)) setNotice("The connection dropped, so it is not certain the reading was saved. What you typed is still here: send it again - it cannot be recorded twice.");
  }

  async function confirmVoid(vitalsId: string) {
    if (busy) return;
    if (reason.trim() === "") {
      setReasonError("Say why the reading is being voided.");
      return;
    }
    setReasonError(null);
    const refused = await run((v) => voidVitals(encounter.id, vitalsId, reason.trim(), v), "Reading voided.");
    if (refused === null) {
      setVoiding(null);
      setReason("");
    } else setReasonError(refused.reason ?? "Could not void the reading.");
  }

  if (!editable && encounter.vitals.length === 0) return null;

  return (
    <section className="alv-clinical__section" aria-labelledby="alv-clinical-vitals-title">
      <h3 id="alv-clinical-vitals-title" className="alv-clinical__section-title">Vital signs</h3>

      {encounter.vitals.length === 0 ? (
        <p className="alv-clinical__meta">No vital signs recorded for this encounter.</p>
      ) : (
        <ul className="alv-clinical__entries">
          {encounter.vitals.map((v) => (
            <li key={v.id} className={`alv-clinical__entry${v.isVoided ? " alv-clinical__entry--voided" : ""}`}>
              <div className="alv-clinical__entry-main">
                <p className="alv-clinical__entry-name">
                  Measured {when(v.measuredAtUtc)} {v.isVoided && <span className="alv-clinical__badge alv-clinical__badge--voided">Voided</span>}
                </p>
                <p className="alv-clinical__entry-details">{describeVitals(v)}{v.note ? ` · ${v.note}` : ""}</p>
                <p className="alv-clinical__meta">
                  Recorded by {v.recordedByName ?? "a staff member"}
                  {v.isVoided ? ` · Voided by ${v.voidedByName ?? "a staff member"}${v.voidedAtUtc ? ` on ${when(v.voidedAtUtc)}` : ""}: ${v.voidReason ?? ""}` : ""}
                </p>
                {voiding === v.id && (
                  <div className="alv-clinical__entry-form">
                    <div className="alv-form-field">
                      <label htmlFor={reasonId} className="alv-form-field__label">Why is this reading being voided?</label>
                      <textarea id={reasonId} className="alv-form-field__input alv-clinical__details" rows={2} maxLength={500} value={reason} aria-invalid={reasonError ? true : undefined} onChange={(e) => setReason(e.target.value)} />
                      {reasonError && <p className="alv-form-field__error" role="alert">{reasonError}</p>}
                    </div>
                    <div className="alv-clinical__row-actions">
                      <Button type="button" variant="primary" disabled={busy} onClick={() => void confirmVoid(v.id)}>Void reading</Button>
                      <Button type="button" disabled={busy} onClick={() => { setVoiding(null); setReason(""); setReasonError(null); }}>Cancel</Button>
                    </div>
                  </div>
                )}
              </div>
              {editable && !v.isVoided && voiding !== v.id && (
                <div className="alv-clinical__row-actions">
                  <Button type="button" disabled={busy} onClick={() => { setVoiding(v.id); setReason(""); setReasonError(null); }} aria-label={`Void the reading measured ${when(v.measuredAtUtc)}`}>Void</Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      {editable && (
        <form className="alv-clinical__entry-form" onSubmit={submit} noValidate aria-label="Record vital signs">
          {errors.vitals && <p className="alv-form-field__error" role="alert">{errors.vitals}</p>}
          {FIELDS.map((f) => (
            <FormField key={f.key} label={f.label} type="text" inputMode="decimal" value={values[f.key]} error={errors[f.key]} hint={f.hint} onChange={(e) => set({ [f.key]: e.target.value })} />
          ))}
          <div className="alv-form-field">
            <label htmlFor={atId} className="alv-form-field__label">Measured at</label>
            <input id={atId} type="datetime-local" className="alv-form-field__input" value={values.measuredAt} aria-invalid={errors.measuredAtUtc ? true : undefined} onChange={(e) => set({ measuredAt: e.target.value })} />
            <p className="alv-form-field__hint">Leave blank for now.</p>
            {errors.measuredAtUtc && <p className="alv-form-field__error" role="alert">{errors.measuredAtUtc}</p>}
          </div>
          <div className="alv-form-field">
            <label htmlFor={noteId} className="alv-form-field__label">Note</label>
            <input id={noteId} className="alv-form-field__input" maxLength={200} value={values.note} aria-invalid={errors.note ? true : undefined} onChange={(e) => set({ note: e.target.value })} />
            {errors.note && <p className="alv-form-field__error" role="alert">{errors.note}</p>}
          </div>
          {notice && <p className="alv-clinical__note" role="alert">{notice}</p>}
          <Button type="submit" variant="primary" disabled={busy || !dirty}>Record vital signs</Button>
        </form>
      )}
    </section>
  );
}
