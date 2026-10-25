import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { briefName, newMergeKey, previewUnmerge, REASON_MAX, unmergeErrorMessage, unmergePatients } from "../../services/patientMergesApi";
import type { UnmergePreview, UnmergeResult } from "../../services/patientMergesApi";

const FIELD_LABELS: Record<string, string> = { MiddleName: "Middle name", Sex: "Sex", Phone: "Phone", Email: "Email", AddressLine1: "Address line 1", AddressLine2: "Address line 2", City: "City", State: "State", PostalCode: "Postal code", Household: "Household" };
const label = (f: string) => FIELD_LABELS[f] ?? f;

interface Props { mergeId: string; onCancel: () => void; onDone: (result: UnmergeResult, preview: UnmergePreview) => void }

/**
 * ALV-017-C01: the review before a merge is undone. The preview is found by really moving the rows back inside a transaction that is rolled back, so what it promises is what the undo does: how many rows go back, which field
 * values are put back and which are kept because someone edited them since, and what stops it (records made after the merge that depend on rows that would move back). The undo needs a reason and an explicit confirmation.
 */
export function UnmergeReview({ mergeId, onCancel, onDone }: Props) {
  const [preview, setPreview] = useState<UnmergePreview | null>(null);
  const [failed, setFailed] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [key] = useState(newMergeKey);

  useEffect(() => {
    const controller = new AbortController();
    setPreview(null); setFailed(null); setConfirmed(false); setError(null);
    previewUnmerge(mergeId, controller.signal).then(setPreview).catch((err) => { if (!controller.signal.aborted) setFailed(unmergeErrorMessage(err)); });
    return () => controller.abort();
  }, [mergeId, attempt]);

  const reasonText = reason.trim();
  const ready = !!preview && preview.canUndo && reasonText.length > 0 && reasonText.length <= REASON_MAX && confirmed && !busy;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!ready || !preview) return;
    setBusy(true); setError(null);
    try {
      onDone(await unmergePatients(mergeId, { reason: reasonText, sourceRowVersion: preview.sourceRowVersion, survivorRowVersion: preview.survivorRowVersion, confirmed: true }, key), preview);
    } catch (err) { setError(unmergeErrorMessage(err)); } finally { setBusy(false); }
  }

  if (failed) return <ErrorState title="This merge cannot be reviewed for an undo" description={failed} action={<Button type="button" onClick={onCancel}>Back to the list</Button>} />;
  if (!preview) return <LoadingState label="Checking what an undo would do…" />;

  const rows = preview.moves.reduce((n, m) => n + m.rows, 0);
  return (
    <form className="alv-fu-form alv-merge" onSubmit={submit} aria-labelledby="alv-unmerge-title" noValidate>
      <h2 id="alv-unmerge-title" className="alv-workspace__section-title">Undo the merge of {briefName(preview.source)} into {briefName(preview.survivor)}</h2>
      <p className="alv-clinical__meta">Undoing moves back exactly the rows this merge moved, makes {briefName(preview.source)} active again, and records who did it and why. The merge stays in the log as history.</p>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
      {error && <Button type="button" onClick={() => setAttempt((a) => a + 1)}>Reload the review</Button>}

      {preview.blockers.length > 0 && (
        <div role="alert" className="alv-fu-error">
          <strong>This merge cannot be undone yet.</strong>
          <ul>{preview.blockers.map((b) => <li key={b.message}>{b.message}</li>)}</ul>
        </div>
      )}

      <h3>What goes back</h3>
      <p>{rows} {rows === 1 ? "row moves" : "rows move"} back to {briefName(preview.source)}.</p>
      <details>
        <summary>Show where they are</summary>
        <ul>{preview.moves.filter((m) => m.rows > 0).map((m) => <li key={`${m.table}.${m.column}`}>{m.table}: {m.rows}</li>)}</ul>
      </details>
      {preview.fieldsToRestore.length > 0 && <p>Put back on {briefName(preview.survivor)}: {preview.fieldsToRestore.map(label).join(", ")}.</p>}
      {preview.fieldsKept.length > 0 && <p className="alv-clinical__meta">Kept as they are now, because they were edited after the merge: {preview.fieldsKept.map(label).join(", ")}.</p>}
      <p className="alv-clinical__meta">A guarantor link from {briefName(preview.survivor)} to {briefName(preview.source)} that the merge cleared is not put back.</p>

      <div className="alv-form-field">
        <label htmlFor="alv-unmerge-reason" className="alv-form-field__label">Reason for the undo *</label>
        <input id="alv-unmerge-reason" className="alv-form-field__input" value={reason} maxLength={REASON_MAX} onChange={(e) => setReason(e.target.value)} autoComplete="off" />
      </div>
      <label className="alv-merge__confirm">
        <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} />
        {" "}I have reviewed what goes back and I confirm that these records are different people and the merge should be undone.
      </label>
      <div className="alv-fu-actions">
        <Button type="submit" variant="primary" disabled={!ready}>Undo the merge</Button>
        <Button type="button" onClick={onCancel}>Back to the list</Button>
      </div>
    </form>
  );
}
