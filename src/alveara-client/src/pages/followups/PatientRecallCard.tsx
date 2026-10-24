import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { INTERVAL_MAX } from "../../services/followUpsApi";
import { getPatientRecall, isStaleQueueChange, queueErrorMessage, resetPatientRecall, setPatientRecall } from "../../services/workQueueApi";
import type { PatientRecall, RecallVerdict } from "../../services/workQueueApi";
import { formatWhen } from "./followUpFormat";
import "./followups.css";

function sentence(r: PatientRecall): string {
  const next = r.nextDueUtc ? formatWhen(r.nextDueUtc) : "";
  const every = `every ${r.effectiveIntervalDays} day${r.effectiveIntervalDays === 1 ? "" : "s"}${r.hasPlan && r.ownIntervalDays ? " (this patient's own interval)" : ""}`;
  const by: Record<RecallVerdict, string> = {
    Due: `Due for a recall: next due ${next}, ${every}.`,
    NotDue: `Next recall due ${next}, ${every}.`,
    OptedOut: "Not recalled automatically: this patient is opted out.",
    NoVisit: "No completed visit yet, so there is no recall date.",
    LapsedPatient: `Not seen for a long time, so not recalled automatically (the next recall would have been ${next}).`,
    OpenRecall: `A recall follow-up is already open (next due ${next}, ${every}).`,
    AlreadyBooked: `Already has a future appointment (next recall ${next}, ${every}).`,
  };
  return by[r.verdict];
}

/**
 * ALV-009-C01: where one patient stands for recall and the controls to arrange it: their own interval (empty means the practice default) and/or not being recalled automatically, with an optional reason, or back to the
 * default. Anyone who may view follow-ups sees it; only those who may manage them see the controls. A change by someone else meanwhile is explained and the card reloaded rather than overwritten.
 */
export function PatientRecallCard({ patientId, canManage, onChanged }: { patientId: string; canManage: boolean; onChanged: () => void }) {
  const [recall, setRecall] = useState<PatientRecall | null>(null);
  const [failed, setFailed] = useState(false);
  const [editing, setEditing] = useState(false);
  const [interval, setInterval] = useState("");
  const [optedOut, setOptedOut] = useState(false);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);

  const load = useCallback((signal?: AbortSignal) => {
    getPatientRecall(patientId, signal).then((r) => { setRecall(r); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, [patientId]);
  useEffect(() => { const c = new AbortController(); load(c.signal); return () => c.abort(); }, [load]);
  useEffect(() => { if (editing) heading.current?.focus(); }, [editing]);

  const open = () => {
    setInterval(recall?.ownIntervalDays ? String(recall.ownIntervalDays) : ""); setOptedOut(recall?.optedOut ?? false); setReason(recall?.reason ?? "");
    setError(null); setStale(false); setStatus(null); setEditing(true);
  };

  async function run(action: () => Promise<PatientRecall>, done: string) {
    setBusy(true); setError(null);
    try { setRecall(await action()); setEditing(false); setStatus(done); onChanged(); }
    catch (err) { if (isStaleQueueChange(err)) setStale(true); setError(queueErrorMessage(err)); }
    finally { setBusy(false); }
  }

  function save(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    const text = interval.trim();
    const days = text === "" ? null : Number(text);
    if (days !== null && (!Number.isInteger(days) || days < 1 || days > INTERVAL_MAX)) { setError(`Enter a whole number of days from 1 to ${INTERVAL_MAX}, or leave it empty for the practice default.`); return; }
    if (days === null && !optedOut) { setError("Choose an interval, or opt the patient out, or use Reset to the practice default."); return; }
    void run(() => setPatientRecall(patientId, { intervalDays: days, optedOut, reason: reason.trim() === "" ? null : reason.trim() }, recall?.rowVersion ?? null), "Saved this patient's recall arrangement.");
  }

  if (failed && recall === null) return <p role="alert" className="alv-fu-error">Could not load this patient&apos;s recall status.</p>;
  if (recall === null) return <p role="status">Loading the recall status…</p>;

  return (
    <section className="alv-fu-recall" aria-labelledby={`alv-fu-recall-${patientId}`}>
      <h3 id={`alv-fu-recall-${patientId}`}>Recall</h3>
      <p>{sentence(recall)}</p>
      {recall.hasPlan && recall.reason && <p className="alv-fu-item__meta">Reason: {recall.reason}</p>}
      {recall.lastCompletedVisitUtc && <p className="alv-fu-item__meta">Last completed visit {formatWhen(recall.lastCompletedVisitUtc)}.</p>}
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? ""}</p>
      {canManage && !editing && <Button type="button" onClick={open}>Arrange this patient&apos;s recall</Button>}
      {editing && (
        <form className="alv-fu-form" onSubmit={save} aria-labelledby={`alv-fu-recall-form-${patientId}`} noValidate>
          <h3 id={`alv-fu-recall-form-${patientId}`} tabIndex={-1} ref={heading}>Arrange this patient&apos;s recall</h3>
          {error && <p role="alert" className="alv-fu-error">{error}</p>}
          {stale && <Button type="button" onClick={() => { setEditing(false); setStale(false); load(); }}>Reload this patient&apos;s recall</Button>}
          <div className="alv-form-field">
            <label htmlFor={`alv-fu-recall-interval-${patientId}`} className="alv-form-field__label">Own interval in days (1 to {INTERVAL_MAX}; empty means the practice default)</label>
            <input id={`alv-fu-recall-interval-${patientId}`} className="alv-form-field__input" inputMode="numeric" value={interval} onChange={(e) => setInterval(e.target.value)} autoComplete="off" />
          </div>
          <label className="alv-policy-check"><input type="checkbox" checked={optedOut} onChange={(e) => setOptedOut(e.target.checked)} /> Do not recall this patient automatically</label>
          <div className="alv-form-field">
            <label htmlFor={`alv-fu-recall-reason-${patientId}`} className="alv-form-field__label">Reason (optional, up to 200 characters)</label>
            <input id={`alv-fu-recall-reason-${patientId}`} className="alv-form-field__input" value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} autoComplete="off" />
          </div>
          <div className="alv-fu-actions">
            <Button type="submit" variant="primary" disabled={busy}>{busy ? "Saving…" : "Save"}</Button>
            {recall.hasPlan && <Button type="button" disabled={busy} onClick={() => void run(() => resetPatientRecall(patientId, recall.rowVersion), "Back to the practice default.")}>Reset to the practice default</Button>}
            <Button type="button" onClick={() => setEditing(false)}>Cancel</Button>
          </div>
        </form>
      )}
    </section>
  );
}
