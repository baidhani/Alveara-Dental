import { useRef, useState } from "react";
import { Button } from "../../components/Button";
import { PageHeader } from "../../components/PageHeader";
import { SafeLink } from "../../components/SafeLink";
import { ApiError } from "../../services/authApi";
import { importPatientFile, ImportRejectedError, portabilityErrorMessage } from "../../services/patientPortabilityApi";
import type { ImportProblem, ImportResult } from "../../services/patientPortabilityApi";
import "../followups/followups.css";

interface Refusal { message: string; existingPatientId: string | null }

/** The place in the file in words: "the file" for the top, otherwise the member name as it is written in the file. */
const placeOf = (path: string) => (path === "$" ? "The whole file" : path);

/**
 * STORY-018: "Import a patient". The person chooses a patient file exported from Alveara and sends it. The server checks the WHOLE file first: if anything is wrong nothing is stored and every problem is shown
 * here with the place in the file and what to do, so the file can be corrected and chosen again. A valid file creates the patient (or, for a file that was imported before, returns the same patient) and the page
 * links to the record; a patient who is already registered is refused with a link to the existing record. Every attempt is logged on the server with the user and the time. Shown only to people who hold the
 * import permission (the administrator and the office manager by default).
 */
export function PatientImportPage() {
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState<ImportResult | null>(null);
  const [problems, setProblems] = useState<ImportProblem[] | null>(null);
  const [refusal, setRefusal] = useState<Refusal | null>(null);
  const [error, setError] = useState<string | null>(null);
  const input = useRef<HTMLInputElement>(null);

  const reset = () => { setDone(null); setProblems(null); setRefusal(null); setError(null); };

  const choose = (chosen: File | null) => { reset(); setFile(chosen); };

  const send = async () => {
    if (!file) return;
    reset(); setBusy(true);
    try {
      setDone(await importPatientFile(file));
    } catch (e) {
      if (e instanceof ImportRejectedError) setProblems(e.problems);
      else if (e instanceof ApiError && (e.code === "duplicate_patient" || e.code === "possible_duplicate")) {
        const existing = (e.body.existingPatientId as string | null | undefined) ?? (Array.isArray(e.body.candidates) ? ((e.body.candidates as { id: string }[])[0]?.id ?? null) : null);
        setRefusal({ message: portabilityErrorMessage(e), existingPatientId: existing });
      } else setError(portabilityErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  const again = () => { reset(); setFile(null); if (input.current) input.current.value = ""; };

  return (
    <div>
      <PageHeader title="Import a patient" description="Bring one patient in from a file exported from Alveara. The whole file is checked first; nothing is stored unless it is correct." />
      <section aria-labelledby="import-choose" className="alv-fu-form">
        <h2 id="import-choose">Choose the file</h2>
        <label htmlFor="import-file">Patient file (.json, up to 256 KB)</label>
        <input id="import-file" ref={input} type="file" accept=".json,application/json" onChange={(e) => choose(e.target.files?.[0] ?? null)} />
        <div className="alv-fu-actions">
          <Button variant="primary" onClick={send} disabled={!file || busy}>{busy ? "Checking the file…" : "Import patient"}</Button>
          {(problems || refusal || error || done) && <Button onClick={again}>Choose another file</Button>}
        </div>
      </section>

      <p className="alv-clinical__status" role="status" aria-live="polite">
        {done ? (done.created ? "The patient was imported." : "This file was imported before. The patient it created is shown; nothing new was created.") : ""}
      </p>
      {done && <p><SafeLink to={`/patients/${done.patientId}`}>Open the patient</SafeLink></p>}

      {problems && (
        <div role="alert" className="alv-fu-error">
          <h2>The file was not imported</h2>
          <p>Correct the problems listed and choose the file again. Nothing was stored.</p>
          <table className="alv-fu-table">
            <caption className="alv-fu-sr">Problems found in the file</caption>
            <thead><tr><th scope="col">Where in the file</th><th scope="col">What is wrong</th></tr></thead>
            <tbody>
              {problems.map((p, i) => <tr key={`${p.path}-${p.code}-${i}`}><td>{placeOf(p.path)}</td><td>{p.message}</td></tr>)}
            </tbody>
          </table>
        </div>
      )}

      {refusal && (
        <div role="alert" className="alv-fu-error">
          <p>{refusal.message}</p>
          {refusal.existingPatientId && <p><SafeLink to={`/patients/${refusal.existingPatientId}`}>Open the existing patient</SafeLink></p>}
        </div>
      )}

      {error && <p role="alert" className="alv-fu-error">{error}</p>}
    </div>
  );
}
