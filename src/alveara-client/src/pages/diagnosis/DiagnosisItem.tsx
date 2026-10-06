import { useEffect, useState } from "react";
import { LoadingState } from "../../components/StatePatterns";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import type { EncounterSummary } from "../../services/clinicalApi";
import { diagnosisProblemsOf, getDiagnosisHistory, withdrawDiagnosis } from "../../services/diagnosisApi";
import type { Diagnosis, DiagnosisVersion } from "../../services/diagnosisApi";
import { displayTooth, toothName } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { DiagnosisForm } from "./DiagnosisForm";

const when = (iso: string) => new Date(iso).toLocaleString();

/** The treatment-plan forward reference as it is shown anywhere: always labelled unresolved, with the plain statement that it proves nothing. */
export function PlanReference({ value }: { value: string | null }) {
  if (value === null) return <span className="alv-clinical__meta">No treatment plan reference.</span>;
  return (
    <span className="alv-dx__reference">
      Treatment plan reference (unresolved): <strong>{value}</strong>
      <span className="alv-clinical__meta"> This is a note to link a plan later; it does not mean a treatment plan with this reference exists.</span>
    </span>
  );
}

interface Props {
  diagnosis: Diagnosis;
  encounters: EncounterSummary[];
  numbering: NumberingSystem;
  canWrite: boolean;
  /** The diagnosis as the server now holds it, after a correction or withdrawal. */
  onChanged: (diagnosis: Diagnosis, text: string) => void;
  onConflict: () => void;
}

/**
 * STORY-013: one diagnosis. It shows what was recorded, for which encounter, by whom and when, and the treatment-plan reference as an unresolved note. People who can write can correct it or withdraw it
 * (both ask why); anyone who can read can open its history, which lists every step with the whole diagnosis as it stood, the reference included. A withdrawn diagnosis stays visible, marked as withdrawn
 * with its reason.
 */
export function DiagnosisItem({ diagnosis: d, encounters, numbering, canWrite, onChanged, onConflict }: Props) {
  const [mode, setMode] = useState<"none" | "correct" | "withdraw" | "history">("none");
  const [reason, setReason] = useState("");
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function withdraw() {
    setBusy(true);
    setNote(null);
    try {
      onChanged(await withdrawDiagnosis(d, reason.trim()), "Diagnosis withdrawn.");
      setMode("none");
      setReason("");
    } catch (err) {
      const problems = diagnosisProblemsOf(err);
      if (isConcurrencyConflict(err)) { setNote("Not withdrawn: someone else changed this diagnosis. Reload and try again."); onConflict(); }
      else if (problems.length > 0) setNote(`Not withdrawn: ${problems.map((p) => p.message).join(" ")}`);
      else if (isNetworkFailure(err)) setNote("Not withdrawn: the connection dropped. Try again.");
      else setNote(`Not withdrawn: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  const encounter = encounters.find((e) => e.id === d.encounterId);
  return (
    <li className={`alv-dx__item${d.status === "Withdrawn" ? " alv-dx__item--withdrawn" : ""}`} aria-label={`Diagnosis: ${d.label}`}>
      <p className="alv-dx__label"><strong>{d.label}</strong>{d.status === "Withdrawn" && <span className="alv-dx__badge"> Withdrawn</span>}</p>
      <p className="alv-clinical__meta">
        {d.toothKey ? `Tooth ${displayTooth(d.toothKey, numbering)} (${toothName(d.toothKey)}) · ` : ""}
        Encounter of {when(d.encounterAtUtc)}{encounter ? ` (${encounter.status.toLowerCase()})` : ""} · recorded by {d.recordedByName ?? "a staff member"}, {when(d.recordedAtUtc)}
        {d.updatedAtUtc ? ` · last changed by ${d.updatedByName ?? "a staff member"}, ${when(d.updatedAtUtc)}` : ""}
      </p>
      {d.notes && <p className="alv-dx__notes">{d.notes}</p>}
      <p><PlanReference value={d.treatmentPlanReference} /></p>
      {d.status === "Withdrawn" && <p className="alv-clinical__meta">Withdrawn by {d.withdrawnByName ?? "a staff member"}{d.withdrawnAtUtc ? `, ${when(d.withdrawnAtUtc)}` : ""}: {d.withdrawnReason}</p>}

      <div className="alv-clinical__row-actions">
        <button type="button" className="btn btn-outline-secondary" aria-expanded={mode === "history"} onClick={() => setMode(mode === "history" ? "none" : "history")}>{mode === "history" ? "Hide history" : "History"}</button>
        {canWrite && d.status === "Active" && (
          <>
            <button type="button" className="btn btn-outline-secondary" onClick={() => setMode(mode === "correct" ? "none" : "correct")}>Correct</button>
            <button type="button" className="btn btn-outline-secondary" onClick={() => setMode(mode === "withdraw" ? "none" : "withdraw")}>Withdraw</button>
          </>
        )}
      </div>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}

      {mode === "correct" && <DiagnosisForm patientId={d.patientId} numbering={numbering} encounters={encounters} correcting={d} onSaved={(saved, text) => { setMode("none"); onChanged(saved, text); }} onConflict={onConflict} onCancel={() => setMode("none")} />}
      {mode === "withdraw" && (
        <form className="alv-dx__form" aria-label="Withdraw this diagnosis" onSubmit={(e) => { e.preventDefault(); void withdraw(); }}>
          <p className="alv-clinical__meta">Withdrawing marks a wrong diagnosis as withdrawn. It stays in the record and its history, treatment plan reference included; nothing is deleted.</p>
          <label>Why is this diagnosis being withdrawn?
            <input type="text" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
          </label>
          <div className="alv-clinical__row-actions">
            <button type="submit" className="btn btn-primary" disabled={busy || reason.trim() === ""}>Withdraw diagnosis</button>
            <button type="button" className="btn btn-outline-secondary" onClick={() => setMode("none")}>Cancel</button>
          </div>
        </form>
      )}
      {mode === "history" && <DiagnosisHistory id={d.id} numbering={numbering} />}
    </li>
  );
}

function DiagnosisHistory({ id }: { id: string; numbering: NumberingSystem }) {
  const [versions, setVersions] = useState<DiagnosisVersion[] | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    getDiagnosisHistory(id, controller.signal).then((h) => setVersions(h.versions)).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [id]);
  if (failed) return <p className="alv-clinical__meta" role="alert">Could not load the history. Do not assume there is none. Close and open it again.</p>;
  if (versions === null) return <LoadingState label="Loading the history…" />;
  return (
    <div className="alv-dx__history">
      <table className="alv-dx__table">
        <caption className="alv-dx__sr">History of this diagnosis, oldest first</caption>
        <thead><tr><th scope="col">When</th><th scope="col">By</th><th scope="col">What happened</th><th scope="col">Diagnosis then</th><th scope="col">Treatment plan reference then</th><th scope="col">Why</th></tr></thead>
        <tbody>
          {versions.map((v) => (
            <tr key={v.versionNumber}>
              <td>{when(v.occurredAtUtc)}</td><td>{v.actorName ?? "a staff member"}</td><td>{v.changeType === "Recorded" ? "Recorded" : v.changeType === "Corrected" ? "Corrected" : "Withdrawn"}</td>
              <td>{v.label}{v.toothKey ? ` (tooth ${v.toothKey} FDI)` : ""}</td>
              <td>{v.treatmentPlanReference === null ? "none" : `${v.treatmentPlanReference} (unresolved)`}</td>
              <td>{v.reason ?? "-"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
