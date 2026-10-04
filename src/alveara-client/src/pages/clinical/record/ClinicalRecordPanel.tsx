import { useCallback, useEffect, useState } from "react";
import { ConcurrencyConflictBanner } from "../../../components/ConcurrencyConflictBanner";
import { ErrorState, LoadingState } from "../../../components/StatePatterns";
import { useAuth } from "../../../contexts/AuthContext";
import { ApiError, isConcurrencyConflict } from "../../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../../services/authApi";
import { clinicalFieldErrorsOf, isNetworkFailure } from "../../../services/clinicalApi";
import { getRecord } from "../../../services/clinicalRecordApi";
import type { ClinicalRecord } from "../../../services/clinicalRecordApi";
import type { PatientDetail } from "../../../services/patientsApi";
import type { SaveResult } from "../SectionPanel";
import { RecordSectionCard } from "./RecordSectionCard";
import "../Clinical.css";

type Saving = { kind: "idle" } | { kind: "saving" } | { kind: "saved"; text: string } | { kind: "failed"; text: string };

/**
 * ALV-005-C01: the patient's clinical record as it stands now, across encounters - medical history, dental history, allergies and medications, each item with a status,
 * who reviewed each section and when, and the full history of every item. This is the history summary beside the encounter notes.
 *
 * Every change is sent as it is made and the record is replaced by what the server returns; the status line says in words whether the last change was saved, is saving or
 * failed. A stale edit shows the shared conflict banner and a reload that refreshes IN PLACE (open forms and typed text stay mounted). Permissions only decide which controls
 * are drawn; they never decide when data is loaded.
 */
export function ClinicalRecordPanel({ patient }: { patient: PatientDetail }) {
  const { hasPermission } = useAuth();
  const canWrite = hasPermission("ManageClinicalNotes");
  const [record, setRecord] = useState<ClinicalRecord | null>(null);
  const [failed, setFailed] = useState(false);
  const [saving, setSaving] = useState<Saving>({ kind: "idle" });
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);

  const adopt = useCallback((next: ClinicalRecord) => {
    setRecord(next);
    setConflict(null);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    getRecord(patient.id, controller.signal)
      .then(adopt)
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true);
      });
    return () => controller.abort();
  }, [patient.id, adopt]);

  const busy = saving.kind === "saving";

  /** Runs one change; null means it was saved, an object means it was not (with the server's per-field messages, if it gave any). */
  async function run(change: () => Promise<ClinicalRecord>, savedText: string): Promise<SaveResult> {
    setSaving({ kind: "saving" });
    try {
      adopt(await change());
      setSaving({ kind: "saved", text: savedText });
      return null;
    } catch (err) {
      if (isConcurrencyConflict(err)) {
        setConflict(err.body);
        setSaving({ kind: "failed", text: "Not saved: someone else changed this record." });
      } else if (isNetworkFailure(err)) {
        setSaving({ kind: "failed", text: "Not saved: the connection dropped. What you typed is still here; try again." });
      } else {
        setSaving({ kind: "failed", text: `Not saved: ${err instanceof ApiError ? err.message : "something went wrong."}` });
      }
      return clinicalFieldErrorsOf(err);
    }
  }

  async function reload() {
    try {
      adopt(await getRecord(patient.id));
      setSaving({ kind: "idle" });
    } catch {
      setSaving({ kind: "failed", text: "Could not reload. Check your connection and try again." });
    }
  }

  if (failed) return <ErrorState title="Could not load the clinical record" description="Check your connection and reload the page." />;
  if (record === null) return <LoadingState label="Loading the clinical record…" />;

  return (
    <section className="alv-clinical alv-clinical__record" aria-labelledby="alv-record-title">
      <h3 id="alv-record-title" className="alv-clinical__section-title">Clinical record</h3>
      <p className="alv-clinical__meta">What is on this patient's chart now, across encounters. A change keeps what it replaced, with who changed it and when.</p>
      <p className="alv-clinical__status" role="status" aria-live="polite">
        {saving.kind === "saving" ? "Saving…" : saving.kind === "idle" ? (canWrite ? "Every change is saved as you make it." : "You can read the record but your role cannot change it.") : saving.text}
      </p>
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => void reload()} />}
      {record.sections.map((section) => (
        <RecordSectionCard key={section.kind} patientId={patient.id} section={section} canWrite={canWrite} busy={busy} run={run} />
      ))}
    </section>
  );
}
