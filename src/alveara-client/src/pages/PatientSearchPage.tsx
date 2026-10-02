import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ButtonLink } from "../components/Button";
import { PageHeader } from "../components/PageHeader";
import { EmptyState, ErrorState, LoadingState } from "../components/StatePatterns";
import { useAuth } from "../contexts/AuthContext";
import { displayName, searchPatients } from "../services/patientsApi";
import type { PatientSummary } from "../services/patientsApi";
import "./PatientWorkspace.css";

type Result = { kind: "loading" } | { kind: "error" } | { kind: "loaded"; rows: PatientSummary[] };
const DEBOUNCE_MS = 250;

/**
 * ALV-003-C01: patient search. Type a name (any part of first or last name, any order), a birth date (yyyy-mm-dd) or a phone
 * number in any format. Inactive patients are hidden unless asked for. Each search cancels the one before it, and a slow earlier
 * answer is never shown after a newer one.
 */
export function PatientSearchPage() {
  const { hasPermission } = useAuth();
  const [query, setQuery] = useState("");
  const [includeInactive, setIncludeInactive] = useState(false);
  const [result, setResult] = useState<Result>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();
    const timer = setTimeout(() => {
      setResult({ kind: "loading" });
      searchPatients(query.trim(), includeInactive, controller.signal)
        .then((rows) => setResult({ kind: "loaded", rows }))
        .catch(() => {
          if (!controller.signal.aborted) setResult({ kind: "error" });
        });
    }, query === "" ? 0 : DEBOUNCE_MS);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [query, includeInactive]);

  return (
    <>
      <PageHeader
        title="Patients"
        description="Find a patient by name, date of birth or phone number."
        actions={hasPermission("RegisterPatients") ? <ButtonLink to="/patients/register" variant="primary">Register new patient</ButtonLink> : undefined}
      />
      <div className="alv-workspace__search">
        <label htmlFor="patient-search" className="alv-workspace__label">Search patients</label>
        <input
          id="patient-search"
          type="search"
          className="alv-form-field__input alv-workspace__search-input"
          value={query}
          autoFocus
          autoComplete="off"
          placeholder="e.g. lee ann, 1985-03-09 or 555-0100"
          onChange={(e) => setQuery(e.target.value)}
        />
        <label className="alv-workspace__checkbox">
          <input type="checkbox" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
          Include inactive patients
        </label>
      </div>

      <p className="alv-workspace__note" role="status" aria-live="polite">
        {result.kind === "loaded" ? `${result.rows.length} ${result.rows.length === 1 ? "patient" : "patients"} shown` : ""}
      </p>

      {result.kind === "loading" && <LoadingState label="Searching…" />}
      {result.kind === "error" && <ErrorState title="Could not search patients" description="Check your connection and try again." />}
      {result.kind === "loaded" && result.rows.length === 0 && (
        <EmptyState title="No patients found" description={query ? "Check the spelling, try fewer words, or include inactive patients." : "No patients have been registered yet."} />
      )}
      {result.kind === "loaded" && result.rows.length > 0 && (
        <table className="alv-workspace__table">
          <caption className="alv-workspace__caption">Patients, in name order</caption>
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Date of birth</th>
              <th scope="col">Age</th>
              <th scope="col">Phone</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            {result.rows.map((p) => (
              <tr key={p.id}>
                <td>
                  <Link to={`/patients/${p.id}`} className="alv-workspace__link" aria-label={displayName(p)}>
                    {p.lastName}, {p.firstName}
                  </Link>
                </td>
                <td>{p.dateOfBirth}</td>
                <td>{p.age}</td>
                <td>{p.phone}</td>
                <td>{p.isActive ? "Active" : <span className="alv-status-badge alv-status-badge--disabled">Inactive</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  );
}
