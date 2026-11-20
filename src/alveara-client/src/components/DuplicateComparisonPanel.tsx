import { useState } from "react";
import { Button } from "./Button";
import { SafeLink } from "./SafeLink";
import type { DuplicateCandidate } from "../services/patientsApi";
import type { PatientValues } from "./patientFields";
import "./DuplicateComparisonPanel.css";

interface Props {
  entered: PatientValues;
  candidates: DuplicateCandidate[];
  /** True when one of the candidates is the same person by name and birth date: registering again is not allowed. */
  blocked: boolean;
  busy?: boolean;
  /** ALV-N025 (D-02a): registering anyway carries the reason the person typed (trimmed, never empty). */
  onRegisterAnyway: (reason: string) => void;
  onDismiss: () => void;
  /** The server's message when it refused the reason (override_reason_required). */
  reasonError?: string | null;
}

/** The server's limit on the reason (PatientRegistrationService.MaxOverrideReasonLength). */
export const OVERRIDE_REASON_MAX = 500;

const nameOf = (p: { firstName: string; middleName?: string | null; lastName: string }) => [p.firstName, p.middleName, p.lastName].filter(Boolean).join(" ");

/**
 * ALV-003-C01: the side-by-side "is this the same person?" panel. The front desk sees what they typed next to each
 * existing patient who might be the same person, with the reason each was flagged. There is no merge: the only choices are to open
 * the existing patient, go back and correct the form, or - when nothing is an exact match - register as a different person.
 */
export function DuplicateComparisonPanel({ entered, candidates, blocked, busy, onRegisterAnyway, onDismiss, reasonError }: Props) {
  const [reason, setReason] = useState("");
  const ready = reason.trim().length > 0;
  return (
    <section className="alv-duplicates" aria-labelledby="alv-duplicates-title">
      <h2 id="alv-duplicates-title" className="alv-duplicates__title">
        {blocked ? "This patient is already registered" : "These patients may be the same person"}
      </h2>
      <p className="alv-duplicates__lead">
        {blocked
          ? "A patient with the same name and date of birth already exists. Open their record instead of registering them again."
          : "Compare them with the patient you are registering. Register anyway only if this is a different person; nothing is merged."}
      </p>
      <div className="alv-duplicates__scroll">
        <table className="alv-duplicates__table">
          <caption className="alv-duplicates__caption">Patient being registered compared with possible matches</caption>
          <thead>
            <tr>
              <th scope="col">Detail</th>
              <th scope="col">Entered now</th>
              {candidates.map((c) => (
                <th scope="col" key={c.id}>
                  Existing: {nameOf(c)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            <tr>
              <th scope="row">Name</th>
              <td>{nameOf(entered)}</td>
              {candidates.map((c) => <td key={c.id}>{nameOf(c)}</td>)}
            </tr>
            <tr>
              <th scope="row">Date of birth</th>
              <td>{entered.dateOfBirth}</td>
              {candidates.map((c) => <td key={c.id}>{c.dateOfBirth}</td>)}
            </tr>
            <tr>
              <th scope="row">Phone</th>
              <td>{entered.phone}</td>
              {candidates.map((c) => <td key={c.id}>{c.phone}</td>)}
            </tr>
            <tr>
              <th scope="row">Email</th>
              <td>{entered.email || "—"}</td>
              {candidates.map((c) => <td key={c.id}>{c.email || "—"}</td>)}
            </tr>
            <tr>
              <th scope="row">City, state</th>
              <td>{entered.city}, {entered.state}</td>
              {candidates.map((c) => <td key={c.id}>{c.city}, {c.state}</td>)}
            </tr>
            <tr>
              <th scope="row">Status</th>
              <td>New</td>
              {candidates.map((c) => <td key={c.id}>{c.isActive ? "Active" : "Inactive"}</td>)}
            </tr>
            <tr>
              <th scope="row">Why flagged</th>
              <td>—</td>
              {candidates.map((c) => (
                <td key={c.id}>
                  <ul className="alv-duplicates__reasons">
                    {c.reasons.map((r) => <li key={r}>{r}</li>)}
                  </ul>
                </td>
              ))}
            </tr>
            <tr>
              <th scope="row">Open record</th>
              <td />
              {candidates.map((c) => (
                <td key={c.id}>
                  <SafeLink to={`/patients/${c.id}`} className="alv-duplicates__link">
                    Open {nameOf(c)}
                  </SafeLink>
                </td>
              ))}
            </tr>
          </tbody>
        </table>
      </div>
      {!blocked && (
        <div className="alv-duplicates__reason">
          <label htmlFor="alv-duplicates-reason">Why is this a different person? (required)</label>
          <p id="alv-duplicates-reason-help" className="alv-duplicates__hint">
            For example: “Mother and daughter with the same name - checked ID”. It is kept in the audit log with the records you compared.
          </p>
          <textarea
            id="alv-duplicates-reason"
            value={reason}
            maxLength={OVERRIDE_REASON_MAX}
            rows={2}
            required
            aria-describedby={reasonError ? "alv-duplicates-reason-help alv-duplicates-reason-error" : "alv-duplicates-reason-help"}
            aria-invalid={reasonError ? true : undefined}
            onChange={(e) => setReason(e.target.value)}
          />
          {reasonError && <p id="alv-duplicates-reason-error" className="alv-duplicates__error" role="alert">{reasonError}</p>}
        </div>
      )}
      <div className="alv-duplicates__actions">
        {!blocked && (
          <Button type="button" variant="primary" onClick={() => ready && onRegisterAnyway(reason.trim())} disabled={busy || !ready}>
            {busy ? "Registering…" : "Register anyway — this is a different person"}
          </Button>
        )}
        <Button type="button" onClick={onDismiss} disabled={busy}>
          Back to the form
        </Button>
      </div>
    </section>
  );
}
