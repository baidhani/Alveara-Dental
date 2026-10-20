import { useId, useState } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { inactivateDocument, problemsOf } from "../../services/documentsApi";
import type { DocumentView } from "../../services/documentsApi";

const REASON_MAX = 500;

/**
 * ALV-010-C01: making a document inactive. A document is never deleted: it is hidden from the library (it can be shown again and reactivated) and the reason, who and when are kept for ever. The reason is
 * required; the form sends the version it was opened with, so a change made by someone else in between is explained instead of overwritten.
 */
export function DocumentInactivateForm({ document, onDone, onCancel, onReload }: { document: DocumentView; onDone: () => void; onCancel: () => void; onReload: () => void }) {
  const id = useId();
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<{ entityType: string; entityId: string } | null>(null);
  const [busy, setBusy] = useState(false);

  async function save() {
    if (busy) return;
    setFormError(null);
    if (!reason.trim()) { setError("Say why the document is being made inactive."); return; }
    if (reason.trim().length > REASON_MAX) { setError(`The reason can be at most ${REASON_MAX} characters.`); return; }
    setError(null);
    setBusy(true);
    try {
      await inactivateDocument(document.id, reason, document.rowVersion);
      onDone();
    } catch (err) {
      if (isConcurrencyConflict(err)) { setConflict(err.body); return; }
      if (!(err instanceof ApiError)) { setFormError("Could not reach the server. Nothing was changed. Check your connection and try again."); return; }
      const problem = problemsOf(err).find((p) => p.field === "reason");
      if (problem) setError(problem.message); else setFormError(err.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-documents__edit" aria-label="Inactivate document" noValidate onSubmit={(e) => { e.preventDefault(); void save(); }}>
      <p className="alv-documents__edit-title">Making {document.originalFileName} inactive</p>
      <p className="alv-workspace__note">The document stays on file and can be shown or made active again. It is hidden from the library until then.</p>
      {conflict && <ConcurrencyConflictBanner problem={{ error: "concurrency_conflict", ...conflict }} onReload={onReload} />}
      <div className="alv-form-field alv-documents__wide">
        <label htmlFor={`${id}-reason`} className="alv-form-field__label">Why is this document being made inactive?</label>
        <textarea id={`${id}-reason`} className={`alv-form-field__input alv-documents__textarea${error ? " alv-form-field__input--error" : ""}`} rows={2} value={reason}
          aria-invalid={error ? true : undefined} aria-describedby={error ? `${id}-error` : undefined} onChange={(e) => setReason(e.target.value)} />
        {error && <p id={`${id}-error`} className="alv-form-field__error" role="alert">{error}</p>}
      </div>
      {formError && <p className="alv-form-field__error alv-documents__wide" role="alert">{formError}</p>}
      <div className="alv-documents__actions alv-documents__wide">
        <Button type="submit" variant="primary" disabled={busy}>{busy ? "Saving…" : "Make inactive"}</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}
