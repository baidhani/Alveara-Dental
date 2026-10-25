import { useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { briefName, isStaleMerge, mergeErrorMessage, mergePatients, newMergeKey, previewMerge, REASON_MAX } from "../../services/patientMergesApi";
import type { FieldChoice, MergePreview, MergeResult, PatientBrief } from "../../services/patientMergesApi";

const FIELD_LABELS: Record<string, string> = {
  MiddleName: "Middle name", Sex: "Sex", Phone: "Phone", Email: "Email", AddressLine1: "Address line 1", AddressLine2: "Address line 2", City: "City", State: "State", PostalCode: "Postal code",
};
const label = (f: string) => FIELD_LABELS[f] ?? f;

interface Props {
  sourceId: string;
  survivorId: string;
  onSwap: () => void;
  onCancel: () => void;
  onMerged: (result: MergeResult, source: PatientBrief, survivor: PatientBrief) => void;
}

/**
 * STORY-017: the side-by-side review of one merge. The retired record and the one that carries on sit next to each other; every field the two fill in differently needs an answer (keep the surviving record's value or take
 * the retired record's); what would move, what has to stay and anything that stops the merge are shown before anything is changed; the merge needs a reason and an explicit confirmation. A record changed meanwhile is explained
 * and the review reloaded rather than merged over. The retired record is never deleted: it stays in place, marked merged.
 */
export function MergeReview({ sourceId, survivorId, onSwap, onCancel, onMerged }: Props) {
  const [preview, setPreview] = useState<MergePreview | null>(null);
  const [failed, setFailed] = useState(false);
  const [choices, setChoices] = useState<Record<string, FieldChoice>>({});
  const [reason, setReason] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const [key, setKey] = useState(newMergeKey);

  useEffect(() => {
    const controller = new AbortController();
    setPreview(null); setFailed(false); setChoices({}); setConfirmed(false); setError(null); setStale(false);
    previewMerge(sourceId, survivorId, controller.signal).then(setPreview).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [sourceId, survivorId, attempt]);

  const moving = useMemo(() => preview?.moves.reduce((n, m) => n + m.rows, 0) ?? 0, [preview]);
  const unanswered = preview ? preview.conflicts.filter((c) => !choices[c.field]).length : 0;
  const reasonText = reason.trim();
  const reasonOk = reasonText.length > 0 && reasonText.length <= REASON_MAX;
  const ready = !!preview && preview.canMerge && unanswered === 0 && reasonOk && confirmed && !busy;

  const choose = (field: string, value: FieldChoice) => { setChoices((c) => ({ ...c, [field]: value })); setKey(newMergeKey()); };

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!ready || !preview) return;
    setBusy(true); setError(null);
    try {
      const result = await mergePatients({ sourcePatientId: sourceId, survivorPatientId: survivorId, reason: reasonText, sourceRowVersion: preview.sourceRowVersion, survivorRowVersion: preview.survivorRowVersion, fieldChoices: choices }, key);
      onMerged(result, preview.source, preview.survivor);
    } catch (err) {
      setError(mergeErrorMessage(err)); setStale(isStaleMerge(err));
    } finally { setBusy(false); }
  }

  if (failed) return <ErrorState title="Could not load the review" description="Check your connection and go back to the list." />;
  if (!preview) return <LoadingState label="Loading the review…" />;

  const { source, survivor } = preview;
  return (
    <form className="alv-fu-form alv-merge" onSubmit={submit} aria-labelledby="alv-merge-title" noValidate>
      <h2 id="alv-merge-title" className="alv-workspace__section-title">Merge {briefName(source)} into {briefName(survivor)}</h2>
      <p className="alv-clinical__meta">The record on the right carries on. The record on the left stays in place, marked merged and inactive, and is never deleted. Everything recorded for it moves to the record that carries on.</p>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
      {stale && <Button type="button" onClick={() => setAttempt((a) => a + 1)}>Reload the review</Button>}

      <table className="alv-fu-table">
        <caption className="alv-fu-sr">The two records side by side</caption>
        <thead><tr><th scope="col">Detail</th><th scope="col">Merged away (retired)</th><th scope="col">Carries on (kept)</th></tr></thead>
        <tbody>
          <tr><th scope="row">Name</th><td>{briefName(source)}</td><td>{briefName(survivor)}</td></tr>
          <tr><th scope="row">Date of birth</th><td>{source.dateOfBirth}</td><td>{survivor.dateOfBirth}</td></tr>
          <tr><th scope="row">Phone</th><td>{source.phone}</td><td>{survivor.phone}</td></tr>
          <tr><th scope="row">Email</th><td>{source.email ?? "None"}</td><td>{survivor.email ?? "None"}</td></tr>
          <tr><th scope="row">City</th><td>{source.city}, {source.state}</td><td>{survivor.city}, {survivor.state}</td></tr>
          <tr><th scope="row">Status</th><td>{source.isActive ? "Active" : "Inactive"}</td><td>{survivor.isActive ? "Active" : "Inactive"}</td></tr>
        </tbody>
      </table>
      <div className="alv-fu-actions"><Button type="button" onClick={onSwap}>Keep the other record instead</Button></div>

      {preview.blockers.length > 0 && (
        <div role="alert" className="alv-fu-error">
          <strong>This merge cannot go ahead yet.</strong>
          <ul>{preview.blockers.map((b) => <li key={b.message}>{b.message}</li>)}</ul>
        </div>
      )}

      {preview.conflicts.length > 0 && (
        <>
          <h3>Fields that differ ({unanswered} still to answer)</h3>
          {preview.conflicts.map((c) => (
            <fieldset key={c.field} className="alv-form-field">
              <legend className="alv-form-field__label">{label(c.field)}</legend>
              <label><input type="radio" name={`choice-${c.field}`} checked={choices[c.field] === "Survivor"} onChange={() => choose(c.field, "Survivor")} /> Keep the kept record's: {c.survivorValue ?? "None"}</label>
              <label><input type="radio" name={`choice-${c.field}`} checked={choices[c.field] === "Source"} onChange={() => choose(c.field, "Source")} /> Take the retired record's: {c.sourceValue ?? "None"}</label>
            </fieldset>
          ))}
        </>
      )}
      {preview.autoFills.length > 0 && (
        <p className="alv-clinical__meta">Filled in from the retired record, because the kept record had nothing: {preview.autoFills.map((f) => `${label(f.field)} (${f.sourceValue})`).join(", ")}.</p>
      )}

      <h3>What moves</h3>
      <p>{moving} {moving === 1 ? "row moves" : "rows move"} to the record that carries on.</p>
      <details>
        <summary>Show where they are</summary>
        <ul>{preview.moves.filter((m) => m.rows > 0).map((m) => <li key={`${m.table}.${m.column}`}>{m.table}: {m.rows}</li>)}</ul>
      </details>
      {preview.leftBehind.length > 0 && (
        <p className="alv-clinical__meta">{preview.leftBehind.length} {preview.leftBehind.length === 1 ? "record stays" : "records stay"} with the retired record, because the kept record already has its own (or depends on one that does). They remain reachable from the merge log.</p>
      )}

      <div className="alv-form-field">
        <label htmlFor="alv-merge-reason" className="alv-form-field__label">Reason for the merge *</label>
        <input id="alv-merge-reason" className="alv-form-field__input" value={reason} maxLength={REASON_MAX} onChange={(e) => setReason(e.target.value)} autoComplete="off" />
      </div>
      <label className="alv-merge__confirm">
        <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} />
        {" "}I have compared the two records. I understand that the retired record stays in place but is marked merged and cannot be edited, and that this cannot be undone from this screen.
      </label>
      <div className="alv-fu-actions">
        <Button type="submit" variant="primary" disabled={!ready}>Merge the records</Button>
        <Button type="button" onClick={onCancel}>Back to the list</Button>
      </div>
      {!ready && !busy && preview.canMerge && <p className="alv-clinical__meta" role="status">{unanswered > 0 ? "Answer every field that differs." : !reasonOk ? "Write the reason." : !confirmed ? "Confirm that you have compared the records." : ""}</p>}
    </form>
  );
}
