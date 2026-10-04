import { Link } from "react-router-dom";
import { useAuth } from "../contexts/AuthContext";
import { usePatientContext } from "../contexts/patientContextStore";
import { displayName } from "../services/patientsApi";
import { SafetyStrip } from "../pages/safety/SafetyStrip";

/**
 * ALV-003-C01: the shared patient identity header, rendered in the shell's patient-context region on every screen.
 * It shows only what the system actually knows about the patient in context (name, birth date and age, sex, phone,
 * inactive status, and the guarantor if one is set). There are deliberately no balances, diagnoses or unsigned-work
 * counts here: those appear when the stories that produce them exist. ALV-N011 adds the persistent patient-safety strip
 * (counts, highest severity and what is not established - never names) for the roles that may read clinical documentation.
 */
export function PatientHeader() {
  const { state, clearPatient } = usePatientContext();
  const { hasPermission } = useAuth();

  return (
    <section className="alv-shell__patient-context" aria-label="Patient context" aria-busy={state.kind === "loading" || undefined}>
      {state.kind === "none" && (
        <p className="alv-patient-header__none">
          No patient selected.{" "}
          {hasPermission("ViewPatientRecords") && (
            <Link to="/patients" className="alv-patient-header__link">
              Find a patient
            </Link>
          )}
        </p>
      )}

      {state.kind === "loading" && <p className="alv-patient-header__none" role="status">Loading patient…</p>}

      {(state.kind === "not-found" || state.kind === "denied" || state.kind === "error") && (
        <p className="alv-patient-header__none" role="alert">
          {state.kind === "not-found" && "That patient was not found."}
          {state.kind === "denied" && "You do not have permission to view patient records."}
          {state.kind === "error" && "The patient could not be loaded."}{" "}
          <button type="button" className="alv-patient-header__button" onClick={clearPatient}>
            Close
          </button>
        </p>
      )}

      {state.kind === "loaded" && (
        <div className="alv-patient-header" data-testid="patient-header">
          <div className="alv-patient-header__identity">
            <span className="alv-patient-header__name">{displayName(state.patient)}</span>
            <span>
              DOB {state.patient.dateOfBirth} ({state.patient.age} y)
            </span>
            {state.patient.sex && <span>{state.patient.sex}</span>}
            <span>{state.patient.phone}</span>
            {!state.patient.isActive && <span className="alv-status-badge alv-status-badge--disabled">Inactive</span>}
            {state.patient.guarantor && <span>Guarantor: {state.patient.guarantor.displayName}</span>}
          </div>
          <div className="alv-patient-header__actions">
            <Link to={`/patients/${state.patientId}`} className="alv-patient-header__link">
              Open workspace
            </Link>
            <button type="button" className="alv-patient-header__button" onClick={clearPatient}>
              Close patient
            </button>
          </div>
          <SafetyStrip patientId={state.patientId} linkClassName="alv-patient-header__link" />
        </div>
      )}
    </section>
  );
}
