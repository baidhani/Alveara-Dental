import { useEffect, useState } from "react";
import { EmptyState, ErrorState, LoadingState } from "../components/StatePatterns";
import { getPatientHistory } from "../services/patientsApi";
import type { PatientDetail, PatientHistoryRow } from "../services/patientsApi";
import "./PatientWorkspace.css";

const FIELD_LABELS: Record<string, string> = {
  firstName: "First name", middleName: "Middle name", lastName: "Last name", dateOfBirth: "Date of birth", sex: "Sex",
  phone: "Phone", email: "Email", addressLine1: "Address", addressLine2: "Address line 2", city: "City", state: "State",
  postalCode: "Postal code", isActive: "Status", guarantorPatientId: "Guarantor", householdId: "Household", householdRelationship: "Household relationship",
};

/** Relationship fields hold other patients' ids; the history says THAT they changed, not an opaque id. */
function describe(row: PatientHistoryRow): { from: string; to: string } {
  const text = (v: string | null) => (v === null || v === "" ? "—" : v);
  if (row.fieldName === "guarantorPatientId" || row.fieldName === "householdId") {
    return { from: row.oldValue ? "Set" : "None", to: row.newValue ? "Set" : "None" };
  }
  if (row.fieldName === "isActive") return { from: row.oldValue === "True" ? "Active" : "Inactive", to: row.newValue === "True" ? "Active" : "Inactive" };
  return { from: text(row.oldValue), to: text(row.newValue) };
}

export function PatientHistoryPanel({ patient }: { patient: PatientDetail }) {
  const [rows, setRows] = useState<PatientHistoryRow[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getPatientHistory(patient.id, controller.signal)
      .then((r) => setRows(r))
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true);
      });
    return () => controller.abort();
  }, [patient.id, patient.rowVersion]);

  if (failed) return <ErrorState title="Could not load the history" />;
  if (rows === null) return <LoadingState label="Loading history…" />;

  return (
    <section aria-labelledby="alv-history-title">
      <h2 id="alv-history-title" className="alv-workspace__section-title">History</h2>
      {rows.length === 0 ? (
        <EmptyState title="No changes yet" description="Edits, status changes and household or guarantor changes appear here with who made them and when." />
      ) : (
        <table className="alv-workspace__table">
          <caption className="alv-workspace__caption">Changes to this patient record, newest first</caption>
          <thead>
            <tr>
              <th scope="col">When</th>
              <th scope="col">Change</th>
              <th scope="col">From</th>
              <th scope="col">To</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => {
              const { from, to } = describe(r);
              return (
                <tr key={r.id}>
                  <td>{new Date(r.changedAtUtc).toLocaleString()}</td>
                  <td>{FIELD_LABELS[r.fieldName] ?? r.fieldName}</td>
                  <td>{from}</td>
                  <td>{to}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
      <p className="alv-workspace__note">Registered {new Date(patient.createdAtUtc).toLocaleString()}.</p>
    </section>
  );
}
