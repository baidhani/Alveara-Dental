import { useId, useState } from "react";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { diagnosisProblemsOf } from "../../services/diagnosisApi";
import type { Diagnosis } from "../../services/diagnosisApi";

interface Props {
  /** The accessible name of the form, e.g. "Withdraw this diagnosis". */
  title: string;
  /** What the person is told before they give the reason. */
  hint: string;
  question: string;
  button: string;
  /** What went unchanged, for the failure message: "withdrawn", "resolved", "reactivated". */
  past: string;
  /** Makes the change; resolves with the diagnosis as the server now holds it. */
  act: (reason: string) => Promise<Diagnosis>;
  onDone: (diagnosis: Diagnosis) => void;
  onConflict: () => void;
  onCancel: () => void;
}

/**
 * A change to a diagnosis that is made for a reason and nothing else: withdrawing it (STORY-013), and resolving or reactivating it (ALV-013-C01). The button stays off until a reason is given; a refusal,
 * a stale change or a dropped connection says so, says the diagnosis was not changed, and keeps the reason that was typed.
 */
export function DiagnosisReasonForm({ title, hint, question, button, past, act, onDone, onConflict, onCancel }: Props) {
  const [reason, setReason] = useState("");
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const id = useId();

  async function submit() {
    setBusy(true);
    setNote(null);
    try {
      onDone(await act(reason.trim()));
    } catch (err) {
      const problems = diagnosisProblemsOf(err);
      if (isConcurrencyConflict(err)) { setNote(`Not ${past}: someone else changed this diagnosis. Reload and try again.`); onConflict(); }
      else if (problems.length > 0) setNote(`Not ${past}: ${problems.map((p) => p.message).join(" ")}`);
      else if (isNetworkFailure(err)) setNote(`Not ${past}: the connection dropped. Try again.`);
      else setNote(`Not ${past}: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label={title} onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <p className="alv-clinical__meta">{hint}</p>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      <label htmlFor={id}>{question}
        <input id={id} type="text" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
      </label>
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || reason.trim() === ""}>{button}</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
