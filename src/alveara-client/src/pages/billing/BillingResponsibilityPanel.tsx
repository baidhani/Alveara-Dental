import { useId, useState } from "react";
import { recordResponsibility } from "../../services/billingApi";
import type { Responsibility } from "../../services/billingApi";
import type { PatientDetail } from "../../services/patientsApi";
import { describeBillingFailure } from "./billingFailure";

interface Props {
  patient: PatientDetail;
  responsibility: Responsibility;
  /** The account's version (a change of responsible party is a change to the account); null until the account exists. */
  accountVersion: string | null;
  canWrite: boolean;
  /** Whether anything has been billed yet. Like the payment and adjustment forms, the form to record who is responsible is offered once the account exists. */
  accountExists: boolean;
  onChanged: (responsibility: Responsibility, text: string) => void;
  onConflict: () => void;
}

const SOURCE: Record<Responsibility["source"], string> = {
  Recorded: "recorded by billing",
  RegisteredGuarantor: "the guarantor registered for this patient, until billing records otherwise",
  Self: "the patient, because no guarantor is registered and none has been recorded",
};

/**
 * ALV-007-C01: who is financially responsible for this account, kept apart from the patient's identity. It follows the patient's registered guarantor until billing records someone, and recording a responsible
 * party never changes the registered guarantor (nor the reverse). People who may change it can record the patient themselves or the registered guarantor; every change stays in the history with who and why.
 */
export function BillingResponsibilityPanel({ patient, responsibility, accountVersion, canWrite, accountExists, onChanged, onConflict }: Props) {
  const [choice, setChoice] = useState(responsibility.responsiblePatientId);
  const [reason, setReason] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const id = useId();
  const guarantor = patient.guarantor && patient.guarantor.isActive ? patient.guarantor : null;

  async function submit() {
    setBusy(true);
    setMessage(null);
    setFieldErrors({});
    try {
      const updated = await recordResponsibility(patient.id, accountVersion, choice, reason.trim() === "" ? null : reason.trim());
      setReason("");
      onChanged(updated, `${updated.responsibleName} is recorded as responsible for this account.`);
    } catch (err) {
      const failure = describeBillingFailure(err);
      setMessage(failure.message.replace("Not recorded:", "Not changed:"));
      setFieldErrors(failure.fields);
      if (failure.conflict) onConflict();
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-labelledby={`${id}-title`} className="alv-bill__responsibility">
      <h3 id={`${id}-title`}>Who is responsible for this account</h3>
      <p>
        <strong>{responsibility.isSelf ? `${responsibility.responsibleName} (the patient)` : responsibility.responsibleName}</strong> - {SOURCE[responsibility.source]}
        {responsibility.effectiveFrom ? `, from ${responsibility.effectiveFrom}` : ""}.
      </p>
      {responsibility.history.length > 0 && (
        <table className="alv-bill__table" aria-label="Who has been responsible">
          <thead><tr><th scope="col">#</th><th scope="col">From</th><th scope="col">Responsible</th><th scope="col">Why</th><th scope="col">Recorded by</th></tr></thead>
          <tbody>
            {responsibility.history.map((h) => (
              <tr key={h.sequence}><td>{h.sequence}</td><td>{h.effectiveFrom}</td><td>{h.isSelf ? `${h.responsibleName} (the patient)` : h.responsibleName}</td><td>{h.reason ?? ""}</td><td>{h.setByName ?? "Unknown"}</td></tr>
            ))}
          </tbody>
        </table>
      )}
      {canWrite && !accountExists && <p className="alv-clinical__meta">Who is responsible can be recorded once something has been billed to this account.</p>}
      {canWrite && accountExists && (
        <form className="alv-dx__form" aria-label="Record who is responsible" noValidate onSubmit={(e) => { e.preventDefault(); void submit(); }}>
          {message && <p className="alv-dx__message" role="alert">{message}</p>}
          <label htmlFor={`${id}-who`}>Responsible party
            <select id={`${id}-who`} value={choice} aria-invalid={fieldErrors.responsiblePatientId ? true : undefined} onChange={(e) => setChoice(e.target.value)}>
              <option value={patient.id}>The patient</option>
              {guarantor && <option value={guarantor.id}>{guarantor.displayName} (registered guarantor)</option>}
              {!responsibility.isSelf && responsibility.responsiblePatientId !== guarantor?.id && <option value={responsibility.responsiblePatientId}>{responsibility.responsibleName} (currently responsible)</option>}
            </select>
          </label>
          {fieldErrors.responsiblePatientId && <p className="alv-bill__error">{fieldErrors.responsiblePatientId}</p>}
          <label htmlFor={`${id}-why`}>Why (optional)
            <input id={`${id}-why`} type="text" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
          </label>
          <div className="alv-clinical__row-actions">
            <button type="submit" className="btn btn-primary" disabled={busy}>Record responsibility</button>
          </div>
        </form>
      )}
    </section>
  );
}
