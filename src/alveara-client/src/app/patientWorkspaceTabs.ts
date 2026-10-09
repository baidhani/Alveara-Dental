/**
 * ALV-003-C01: the patient workspace's navigation extension point.
 *
 * Each entry is one tab of the patient workspace (/patients/:patientId/<path>). A later story (clinical notes, treatment
 * plans, documents, billing, tasks...) adds its tab by appending an entry here and a matching child <Route> in App.tsx -
 * it does not build its own patient shell. Only modules that actually exist are listed: no placeholder tabs, no fake
 * counts, balances, alerts or unsigned-work badges.
 */
export interface PatientWorkspaceTab {
  id: string;
  label: string;
  /** Path under /patients/:patientId ("" = the workspace's landing tab). */
  path: string;
  /** If set, the tab is shown (and its route reachable) only with this permission; the server re-checks every call. */
  requiredPermission?: string;
}

export const patientWorkspaceTabs: PatientWorkspaceTab[] = [
  { id: "details", label: "Details", path: "" },
  { id: "household", label: "Household & guarantor", path: "household" },
  { id: "history", label: "History", path: "history" },
  // ALV-N010: forms and consents (shown only to roles that may view them).
  { id: "forms", label: "Forms", path: "forms", requiredPermission: "ViewSignedForms" },
  // STORY-005: clinical documentation (medical and dental history, allergies, medications); shown only to roles that may read it - not to front desk or billing.
  { id: "clinical", label: "Clinical", path: "clinical", requiredPermission: "ViewClinicalDocumentation" },
  // ALV-N011: patient safety - alerts, allergies and medications as a safety view, and clearances; the same roles that read clinical documentation.
  { id: "safety", label: "Safety", path: "safety", requiredPermission: "ViewClinicalDocumentation" },
  // STORY-006: the odontogram (tooth chart); the same roles that read clinical documentation.
  { id: "odontogram", label: "Odontogram", path: "odontogram", requiredPermission: "ViewClinicalDocumentation" },
  // STORY-012: periodontal charting (probing depth, recession, bleeding); the same roles that read clinical documentation.
  { id: "perio", label: "Periodontal", path: "periodontal", requiredPermission: "ViewClinicalDocumentation" },
  // STORY-013: structured diagnoses linked to the patient and an encounter, with an optional unresolved treatment-plan reference; the same roles that read clinical documentation.
  { id: "diagnoses", label: "Diagnoses", path: "diagnoses", requiredPermission: "ViewClinicalDocumentation" },
  // STORY-015: treatment plans (diagnosis-linked procedures with catalog fee estimates); the same roles that read clinical documentation may read them, only dentists and administrators change them.
  { id: "treatment-plan", label: "Treatment plan", path: "treatment-plan", requiredPermission: "ViewClinicalDocumentation" },
];
