import { useCallback, useEffect, useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { FormField } from "../../components/FormField";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import { clinicalFieldErrorsOf, isNetworkFailure } from "../../services/clinicalApi";
import type { PatientDetail } from "../../services/patientsApi";
import { CLEARANCE_KINDS, announceSafetyChanged, createAlert, getSafety, requestClearance } from "../../services/safetyApi";
import type { SafetyContext } from "../../services/safetyApi";
import { ClearanceCard } from "./ClearanceCard";
import { AlertForm } from "./SafetyForms";
import type { SaveResult } from "./SafetyForms";
import { SafetyEntryRow } from "./SafetyEntryRow";
import { when } from "./safetyText";
import "./Safety.css";

type Saving = { kind: "idle" } | { kind: "saving" } | { kind: "saved"; text: string } | { kind: "failed"; text: string };

function ClearanceRequestForm({ busy, onSubmit, onCancel }: { busy: boolean; onSubmit: (kind: string, reason: string, from: string) => Promise<SaveResult>; onCancel: () => void }) {
  const [kind, setKind] = useState("Medical");
  const [reason, setReason] = useState("");
  const [from, setFrom] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const kindId = useId();
  const reasonId = useId();
  useUnsavedChangesWarning(reason.trim() !== "" || from.trim() !== "");

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    if (reason.trim() === "") {
      setErrors({ reason: "Say why the clearance is needed." });
      return;
    }
    setErrors({});
    const refused = await onSubmit(kind, reason, from);
    if (refused) setErrors(refused);
  }

  return (
    <form className="alv-safety__form" onSubmit={submit} noValidate aria-label="Request a clearance">
      <div className="alv-form-field">
        <label htmlFor={kindId} className="alv-form-field__label">Kind of clearance</label>
        <select id={kindId} className="alv-form-field__input" value={kind} onChange={(e) => setKind(e.target.value)}>
          {CLEARANCE_KINDS.map((k) => <option key={k} value={k}>{k}</option>)}
        </select>
        {errors.kind && <p className="alv-form-field__error" role="alert">{errors.kind}</p>}
      </div>
      <div className="alv-form-field">
        <label htmlFor={reasonId} className="alv-form-field__label">Why is it needed?</label>
        <textarea id={reasonId} className="alv-form-field__input alv-clinical__details" rows={2} maxLength={500} value={reason} aria-invalid={errors.reason ? true : undefined} onChange={(e) => setReason(e.target.value)} />
        {errors.reason && <p className="alv-form-field__error" role="alert">{errors.reason}</p>}
      </div>
      <FormField label="Requested from (optional)" value={from} error={errors.requestedFrom} maxLength={200} onChange={(e) => setFrom(e.target.value)} />
      <div className="alv-clinical__row-actions">
        <Button type="submit" variant="primary" disabled={busy}>Request clearance</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}

/**
 * ALV-N011: the patient's safety picture before diagnosis, planning, treatment and prescribing - what is on file and, just as important, what is NOT established.
 * It shows: the gaps (a record section never reviewed, unknown, or changed since it was confirmed - each says "nothing listed here does not mean none"); the active entries, most
 * urgent first (allergies and current medications READ from the clinical record, and the alerts a clinician stated, each with source, status and last update); the clearances and
 * their workflow; and the resolved alerts, kept with who resolved them and why.
 *
 * Acknowledging records that the signed-in person saw an alert and never resolves it. Every change is sent as it is made and the picture is replaced by what the server returns;
 * the status line says in words whether the last change was saved, is saving or failed. A stale edit shows the shared conflict banner and a reload that refreshes in place, so
 * open forms keep what was typed. Permissions only decide which controls are drawn; they never decide when data is loaded.
 */
export function SafetyPanel({ patient }: { patient: PatientDetail }) {
  const { hasPermission } = useAuth();
  const canWrite = hasPermission("ManageClinicalNotes");
  const canAcknowledge = hasPermission("ViewClinicalDocumentation");
  const [context, setContext] = useState<SafetyContext | null>(null);
  const [failed, setFailed] = useState(false);
  const [saving, setSaving] = useState<Saving>({ kind: "idle" });
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const [addingAlert, setAddingAlert] = useState(false);
  const [requesting, setRequesting] = useState(false);

  const adopt = useCallback((next: SafetyContext) => {
    setContext(next);
    setConflict(null);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    getSafety(patient.id, controller.signal).then(adopt).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [patient.id, adopt]);

  const busy = saving.kind === "saving";

  async function run(change: () => Promise<SafetyContext>, savedText: string): Promise<SaveResult> {
    setSaving({ kind: "saving" });
    try {
      adopt(await change());
      setSaving({ kind: "saved", text: savedText });
      announceSafetyChanged(patient.id);
      return null;
    } catch (err) {
      if (isConcurrencyConflict(err)) {
        setConflict(err.body);
        setSaving({ kind: "failed", text: "Not saved: someone else changed this." });
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
      adopt(await getSafety(patient.id));
      setSaving({ kind: "idle" });
      announceSafetyChanged(patient.id);
    } catch {
      setSaving({ kind: "failed", text: "Could not reload. Check your connection and try again." });
    }
  }

  if (failed) return <ErrorState title="Could not load the patient's safety information" description="Do not assume there is none. Check your connection and reload the page." />;
  if (context === null) return <LoadingState label="Loading patient safety information…" />;

  return (
    <section className="alv-clinical alv-safety" aria-labelledby="alv-safety-title">
      <h2 id="alv-safety-title" className="alv-workspace__section-title">Patient safety details</h2>
      <p className="alv-clinical__meta">
        What to know before diagnosis, planning, treatment and prescribing, as of {when(context.asOfUtc)}. Allergies and current medications are read from the clinical record; alerts are what a
        clinician stated, with their source. Acknowledging an alert records that you saw it - it does not resolve it.
      </p>
      <p className="alv-clinical__status" role="status" aria-live="polite">
        {saving.kind === "saving" ? "Saving…" : saving.kind === "idle" ? (canWrite ? "Every change is saved as you make it." : "You can read this but your role cannot change it.") : saving.text}
      </p>
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => void reload()} />}

      {context.gaps.length > 0 && (
        <section className="alv-safety__gaps" aria-labelledby="alv-safety-gaps">
          <h3 id="alv-safety-gaps" className="alv-clinical__section-title">Not established</h3>
          <ul>
            {context.gaps.map((g) => <li key={g.section}>{g.message}</li>)}
          </ul>
        </section>
      )}

      <section className="alv-clinical__section" aria-labelledby="alv-safety-active">
        <h3 id="alv-safety-active" className="alv-clinical__section-title">Active alerts and safety information</h3>
        {context.entries.length === 0 ? (
          <p className="alv-clinical__meta">No alerts, active allergies or current medications are recorded. This is not a statement that there are none.</p>
        ) : (
          <ul className="alv-clinical__entries">
            {context.entries.map((e) => (
              <SafetyEntryRow key={`${e.origin}-${e.id}`} entry={e} patientId={patient.id} canWrite={canWrite} canAcknowledge={canAcknowledge} busy={busy} run={run} />
            ))}
          </ul>
        )}
        {canWrite && (
          addingAlert ? (
            <AlertForm
              submitLabel="Add alert"
              busy={busy}
              onCancel={() => setAddingAlert(false)}
              onSubmit={async (input) => {
                const refused = await run(() => createAlert(patient.id, input), `${input.title.trim()} added.`);
                if (!refused) setAddingAlert(false);
                return refused;
              }}
            />
          ) : (
            <div className="alv-clinical__section-actions"><Button type="button" onClick={() => setAddingAlert(true)} disabled={busy}>Add an alert</Button></div>
          )
        )}
      </section>

      <section className="alv-clinical__section" aria-labelledby="alv-safety-clearances">
        <h3 id="alv-safety-clearances" className="alv-clinical__section-title">Clearances</h3>
        {context.clearances.length === 0 ? (
          <p className="alv-clinical__meta">No clearances have been requested.</p>
        ) : (
          <ul className="alv-clinical__entries">
            {context.clearances.map((c) => <ClearanceCard key={c.id} clearance={c} canWrite={canWrite} busy={busy} run={run} />)}
          </ul>
        )}
        {canWrite && (
          requesting ? (
            <ClearanceRequestForm
              busy={busy}
              onCancel={() => setRequesting(false)}
              onSubmit={async (kind, reason, from) => {
                const refused = await run(() => requestClearance(patient.id, kind, reason, from), `${kind} clearance requested.`);
                if (!refused) setRequesting(false);
                return refused;
              }}
            />
          ) : (
            <div className="alv-clinical__section-actions"><Button type="button" onClick={() => setRequesting(true)} disabled={busy}>Request a clearance</Button></div>
          )
        )}
      </section>

      {context.resolved.length > 0 && (
        <details className="alv-clinical__history alv-safety__resolved">
          <summary>Resolved alerts ({context.resolved.length})</summary>
          <ul className="alv-clinical__entries">
            {context.resolved.map((e) => (
              <SafetyEntryRow key={`${e.origin}-${e.id}`} entry={e} patientId={patient.id} canWrite={canWrite} canAcknowledge={false} busy={busy} run={run} />
            ))}
          </ul>
        </details>
      )}
    </section>
  );
}
