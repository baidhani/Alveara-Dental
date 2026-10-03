import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "../../components/Button";
import { SafeLink } from "../../components/SafeLink";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { ApiError } from "../../services/authApi";
import { isNetworkFailure, listEncounters, newKey, startEncounter } from "../../services/clinicalApi";
import type { EncounterSummary } from "../../services/clinicalApi";
import type { PatientDetail } from "../../services/patientsApi";
import "./Clinical.css";

const when = (iso: string) => new Date(iso).toLocaleString();

/**
 * STORY-005: the patient workspace's Clinical tab - the patient's encounters, newest first, each said in words to be a draft or finalized and whether all four sections
 * (medical history, dental history, allergies, medications) have been addressed; and, for a clinician, starting a new one. The start request carries one key that is kept
 * across retries, so a double click or a dropped connection can never create two encounters.
 */
export function PatientClinicalPanel({ patient }: { patient: PatientDetail }) {
  const { hasPermission } = useAuth();
  const navigate = useNavigate();
  const canWrite = hasPermission("ManageClinicalNotes");
  const [encounters, setEncounters] = useState<EncounterSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const key = useRef<string | null>(null);

  // Loaded once per patient; the permission only decides which controls are drawn, never whether the list is fetched again.
  useEffect(() => {
    const controller = new AbortController();
    listEncounters(patient.id, controller.signal)
      .then(setEncounters)
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true);
      });
    return () => controller.abort();
  }, [patient.id]);

  async function start() {
    if (busy) return;
    setBusy(true);
    setError(null);
    key.current ??= newKey();
    try {
      const started = await startEncounter(patient.id, key.current);
      key.current = null;
      navigate(`/patients/${patient.id}/clinical/${started.id}`);
    } catch (err) {
      if (err instanceof ApiError) key.current = null;
      setError(
        isNetworkFailure(err)
          ? "The connection dropped, so it is not certain the encounter was started. Try again - it cannot be started twice."
          : err instanceof ApiError ? err.message : "Could not start the encounter. Try again.",
      );
    } finally {
      setBusy(false);
    }
  }

  if (failed) return <ErrorState title="Could not load the encounters" />;
  if (encounters === null) return <LoadingState label="Loading encounters…" />;

  return (
    <section aria-labelledby="alv-clinical-list-title">
      <h2 id="alv-clinical-list-title" className="alv-workspace__section-title">Clinical documentation</h2>
      {canWrite && (
        <div className="alv-clinical__start">
          <Button type="button" variant="primary" onClick={() => void start()} disabled={busy || !patient.isActive}>Start an encounter</Button>
          {!patient.isActive && <p className="alv-clinical__note">This patient is inactive. Reactivate them before documenting a new encounter.</p>}
          {error && <p className="alv-form-field__error" role="alert">{error}</p>}
        </div>
      )}
      {encounters.length === 0 ? (
        <EmptyState title="No encounters documented yet" description={canWrite ? "Start an encounter to record medical and dental history, allergies and medications." : "Nothing has been documented for this patient."} />
      ) : (
        <ul className="alv-clinical__list">
          {encounters.map((e) => (
            <li key={e.id} className="alv-clinical__list-item">
              <SafeLink to={`/patients/${patient.id}/clinical/${e.id}`} className="alv-workspace__link alv-clinical__list-link">Encounter on {when(e.encounterAtUtc)}</SafeLink>
              <span className={`alv-clinical__badge alv-clinical__badge--${e.status.toLowerCase()}`}>{e.status === "Draft" ? "Draft - not finalized" : "Finalized"}</span>
              <span className="alv-clinical__meta">
                {e.isComplete ? "All four sections addressed" : "Some sections not yet addressed"} · {e.entryCount} {e.entryCount === 1 ? "entry" : "entries"}
                {e.addendumCount > 0 ? ` · ${e.addendumCount} ${e.addendumCount === 1 ? "addendum" : "addenda"}` : ""}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
