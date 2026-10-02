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
];
