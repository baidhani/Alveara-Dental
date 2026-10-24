import { useCallback, useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { INTERVAL_MAX } from "../../services/followUpsApi";
import { getRecallSettings, isStaleQueueChange, queueErrorMessage, updateRecallSettings } from "../../services/workQueueApi";
import type { RecallSettings } from "../../services/workQueueApi";
import "./followups.css";

interface Field { key: "defaultIntervalDays" | "leadDays" | "maxVisitAgeMonths" | "dueSoonDays"; label: string; hint: string; min: number; max: number }

const FIELDS: Field[] = [
  { key: "defaultIntervalDays", label: "Default recall interval (days)", hint: "How long after a completed visit a patient comes due, unless they have their own interval.", min: 1, max: INTERVAL_MAX },
  { key: "leadDays", label: "Make the recall task this many days before the due date", hint: "0 makes it on the due date itself.", min: 0, max: 365 },
  { key: "maxVisitAgeMonths", label: "Stop recalling automatically after this many months without a visit", hint: "A patient not seen for longer has lapsed; the front desk decides whether to reach them.", min: 1, max: 120 },
  { key: "dueSoonDays", label: "Show a task as due soon this many days ahead", hint: "Used by the work queue's Due soon filter.", min: 1, max: 30 },
];

/**
 * ALV-009-C01: the practice's recall settings. Automatic recall is OFF until the practice turns it on here, so switching the system on never fills the work queue with a backlog. The numbers are held to the same limits as the
 * server (each field says its limits and a refused value is said in words beside the field); a change made by someone else meanwhile is explained and the settings reloaded rather than overwritten.
 */
export function RecallSettingsPage() {
  const [saved, setSaved] = useState<RecallSettings | null>(null);
  const [failed, setFailed] = useState(false);
  const [enabled, setEnabled] = useState(false);
  const [values, setValues] = useState<Record<Field["key"], string>>({ defaultIntervalDays: "", leadDays: "", maxVisitAgeMonths: "", dueSoonDays: "" });
  const [errors, setErrors] = useState<Partial<Record<Field["key"], string>>>({});
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);

  const adopt = useCallback((s: RecallSettings) => {
    setSaved(s); setEnabled(s.enabled);
    setValues({ defaultIntervalDays: String(s.defaultIntervalDays), leadDays: String(s.leadDays), maxVisitAgeMonths: String(s.maxVisitAgeMonths), dueSoonDays: String(s.dueSoonDays) });
  }, []);
  const load = useCallback((signal?: AbortSignal) => { getRecallSettings(signal).then((s) => { adopt(s); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); }); }, [adopt]);
  useEffect(() => { const c = new AbortController(); load(c.signal); return () => c.abort(); }, [load]);

  async function save(e: FormEvent) {
    e.preventDefault();
    if (busy || !saved) return;
    const found: Partial<Record<Field["key"], string>> = {};
    const numbers = {} as Record<Field["key"], number>;
    for (const f of FIELDS) {
      const text = values[f.key].trim();
      const n = Number(text);
      if (text === "" || !Number.isInteger(n) || n < f.min || n > f.max) found[f.key] = `Enter a whole number from ${f.min} to ${f.max}.`;
      else numbers[f.key] = n;
    }
    setErrors(found);
    if (Object.keys(found).length > 0) { setError("Fix the marked fields and save again."); return; }
    setBusy(true); setError(null); setStatus(null);
    try {
      adopt(await updateRecallSettings({ enabled, ...numbers }, saved.rowVersion));
      setStale(false);
      setStatus("Saved the recall settings.");
    } catch (err) {
      if (isStaleQueueChange(err)) setStale(true);
      setError(queueErrorMessage(err));
    } finally { setBusy(false); }
  }

  if (failed && saved === null) return <ErrorState title="Could not load the recall settings" description="Check your connection and reload the page." />;
  if (saved === null) return <LoadingState label="Loading the recall settings…" />;

  return (
    <section className="alv-clinical alv-fu" aria-labelledby="alv-recall-title">
      <h1 id="alv-recall-title" className="alv-workspace__section-title">Recall settings</h1>
      <p className="alv-clinical__meta">
        When automatic recall is on, the system makes a recall follow-up for each patient who is coming due, who has not opted out and who has nothing booked. It is off until you turn it on. A patient can have their own interval or be
        opted out from their follow-ups page.
      </p>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? (saved.enabled ? "Automatic recall is on." : "Automatic recall is off.")}</p>
      <form className="alv-fu-form" onSubmit={save} noValidate aria-label="Recall settings">
        {error && <p role="alert" className="alv-fu-error">{error}</p>}
        {stale && <Button type="button" onClick={() => { setStale(false); setError(null); load(); }}>Reload the recall settings</Button>}
        <label className="alv-policy-check"><input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} /> Make recall follow-ups automatically</label>
        {FIELDS.map((f) => (
          <div className="alv-form-field" key={f.key}>
            <label htmlFor={`recall-${f.key}`} className="alv-form-field__label">{f.label}</label>
            <input id={`recall-${f.key}`} className="alv-form-field__input" inputMode="numeric" value={values[f.key]} onChange={(e) => setValues((v) => ({ ...v, [f.key]: e.target.value }))}
              aria-invalid={!!errors[f.key]} aria-describedby={`recall-${f.key}-hint`} autoComplete="off" />
            <p id={`recall-${f.key}-hint`} className={errors[f.key] ? "alv-fu-error" : "alv-fu-item__meta"}>{errors[f.key] ?? `${f.hint} (${f.min} to ${f.max}.)`}</p>
          </div>
        ))}
        <div className="alv-fu-actions"><Button type="submit" variant="primary" disabled={busy}>{busy ? "Saving…" : "Save the recall settings"}</Button></div>
      </form>
    </section>
  );
}
