import { useEffect, useId, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "../../components/Button";
import { SafeLink } from "../../components/SafeLink";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { ApiError } from "../../services/authApi";
import { CATEGORY_LABELS, listAvailableTemplates, listPatientForms, startPatientForm } from "../../services/formsApi";
import type { PatientFormSummary, TemplateSummary } from "../../services/formsApi";
import type { PatientDetail } from "../../services/patientsApi";
import { FormStatusBadge } from "./FormStatusBadge";
import "./Forms.css";

const when = (iso: string | null) => (iso ? new Date(iso).toLocaleString() : "—");

/**
 * ALV-N010: the patient workspace's Forms tab - the patient's forms with their status, and (for staff who complete forms) starting a new
 * one from an active template. Unsigned drafts are listed first-class beside signed and void forms; nothing is implied signed.
 */
export function PatientFormsPanel({ patient }: { patient: PatientDetail }) {
  const { hasPermission } = useAuth();
  const navigate = useNavigate();
  const canComplete = hasPermission("CompleteForms");
  const [forms, setForms] = useState<PatientFormSummary[] | null>(null);
  const [templates, setTemplates] = useState<TemplateSummary[]>([]);
  const [failed, setFailed] = useState(false);
  const [chosen, setChosen] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const selectId = useId();

  useEffect(() => {
    const controller = new AbortController();
    listPatientForms(patient.id, controller.signal)
      .then(setForms)
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true);
      });
    if (canComplete) {
      listAvailableTemplates(controller.signal)
        .then(setTemplates)
        .catch(() => {
          if (!controller.signal.aborted) setTemplates([]);
        });
    }
    return () => controller.abort();
  }, [patient.id, canComplete]);

  async function start() {
    if (!chosen || busy) return;
    setBusy(true);
    setError(null);
    try {
      const started = await startPatientForm(patient.id, chosen);
      navigate(`/patients/${patient.id}/forms/${started.summary.id}`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not start the form. Check your connection and try again.");
    } finally {
      setBusy(false);
    }
  }

  if (failed) return <ErrorState title="Could not load the forms" />;
  if (forms === null) return <LoadingState label="Loading forms…" />;

  return (
    <section aria-labelledby="alv-forms-title">
      <h2 id="alv-forms-title" className="alv-workspace__section-title">Forms and consents</h2>
      {canComplete && (
        patient.isActive ? (
          <div className="alv-forms__start">
            <div className="alv-form-field">
              <label htmlFor={selectId} className="alv-form-field__label">Start a form</label>
              <select id={selectId} className="alv-form-field__input" value={chosen} onChange={(e) => setChosen(e.target.value)} disabled={busy}>
                <option value="">Choose a form…</option>
                {templates.map((t) => (
                  <option key={t.id} value={t.id}>{t.current?.title ?? t.key} ({CATEGORY_LABELS[t.category] ?? t.category})</option>
                ))}
              </select>
            </div>
            <Button variant="primary" onClick={() => void start()} disabled={!chosen || busy}>{busy ? "Starting…" : "Start form"}</Button>
          </div>
        ) : (
          <p className="alv-workspace__note">This patient is inactive. Reactivate them to start a new form.</p>
        )
      )}
      {error && <p className="alv-form-field__error" role="alert">{error}</p>}
      {forms.length === 0 ? (
        <EmptyState title="No forms yet" description="Forms and consents started for this patient appear here, with whether each is a draft, signed or void." />
      ) : (
        <table className="alv-workspace__table">
          <caption className="alv-workspace__caption">This patient's forms, newest first</caption>
          <thead>
            <tr>
              <th scope="col">Form</th>
              <th scope="col">Category</th>
              <th scope="col">Version</th>
              <th scope="col">Status</th>
              <th scope="col">Signed / started</th>
              <th scope="col">Signer</th>
            </tr>
          </thead>
          <tbody>
            {forms.map((f) => (
              <tr key={f.id}>
                <td><SafeLink to={`/patients/${patient.id}/forms/${f.id}`} className="alv-workspace__link">{f.title}</SafeLink></td>
                <td>{CATEGORY_LABELS[f.category] ?? f.category}</td>
                <td>{f.templateVersionNumber}</td>
                <td>
                  <FormStatusBadge status={f.status} wasSigned={f.wasSigned} />
                  {f.newerVersionAvailable && <span className="alv-workspace__note"> · newer version available</span>}
                </td>
                <td>{f.signedAtUtc ? when(f.signedAtUtc) : `Started ${when(f.startedAtUtc)}`}</td>
                <td>{f.signerName ? `${f.signerName} (${f.signerRelationship})` : "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
