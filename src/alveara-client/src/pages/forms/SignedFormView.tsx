import { FormAnswers } from "../../components/FormFieldInputs";
import { CATEGORY_LABELS } from "../../services/formsApi";
import type { FormEvent, PatientFormDetail } from "../../services/formsApi";
import "./Forms.css";

const EVENT_LABELS: Record<string, string> = { Started: "Started", Signed: "Signed", Voided: "Voided", Superseded: "Replaced by a newer version" };

const when = (iso: string) => new Date(iso).toLocaleString();

export function FormHistory({ events }: { events: FormEvent[] }) {
  return (
    <table className="alv-workspace__table">
      <caption className="alv-workspace__caption">Status history of this form, oldest first</caption>
      <thead>
        <tr><th scope="col">When</th><th scope="col">Event</th><th scope="col">Template version</th><th scope="col">Details</th></tr>
      </thead>
      <tbody>
        {events.map((e, i) => (
          <tr key={`${e.eventType}-${i}`}>
            <td>{when(e.occurredAtUtc)}</td>
            <td>{EVENT_LABELS[e.eventType] ?? e.eventType}</td>
            <td>{e.templateVersionNumber}</td>
            <td>{e.detail ?? "—"}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/**
 * ALV-N010: a signed form exactly as it was signed - the template version's wording and the answers shown at signing, who signed and how
 * they relate to the patient, when, and an integrity check that the stored copy still matches what was signed. Read-only.
 */
export function SignedFormView({ form }: { form: PatientFormDetail }) {
  const s = form.snapshot!;
  return (
    <section aria-labelledby="alv-signed-title" className="alv-workspace__panel">
      <h2 id="alv-signed-title" className="alv-workspace__section-title">Signed copy</h2>
      <div className="alv-forms__meta">
        <span>{CATEGORY_LABELS[s.category] ?? s.category}</span>
        <span>Template version {s.templateVersionNumber}</span>
        <span>Signed {when(s.signedAtUtc)}</span>
        <span role="status">{s.integrityVerified ? "Integrity check: matches the copy made at signing." : "Integrity check FAILED: this copy no longer matches what was signed."}</span>
      </div>
      <div>
        <h3 className="alv-workspace__subtitle">What the form said</h3>
        <div className="alv-forms__wording" tabIndex={0} role="region" aria-label="Signed form wording">{s.body}</div>
      </div>
      <div>
        <h3 className="alv-workspace__subtitle">Answers at signing</h3>
        <FormAnswers fields={s.fields} responses={s.responses} />
      </div>
      <div>
        <h3 className="alv-workspace__subtitle">Signature</h3>
        <dl className="alv-form-answers">
          <div className="alv-form-answers__row"><dt>Signed by</dt><dd>{s.signerName}</dd></div>
          <div className="alv-form-answers__row"><dt>Relationship to the patient</dt><dd>{s.signerRelationship}{s.signerRelationshipNote ? ` - ${s.signerRelationshipNote}` : ""}</dd></div>
          <div className="alv-form-answers__row"><dt>Typed signature</dt><dd>{s.signatureText}</dd></div>
          <div className="alv-form-answers__row"><dt>Statement agreed to</dt><dd>{s.attestation}</dd></div>
          <div className="alv-form-answers__row"><dt>Record fingerprint (SHA-256)</dt><dd className="alv-forms__hash">{s.snapshotHash}</dd></div>
        </dl>
      </div>
    </section>
  );
}
