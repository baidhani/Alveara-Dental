/**
 * ALV-N006: the reports the hub offers. A report is listed only for a person who holds the permission of its group (the server re-checks every call). `providerFilter` and `locationFilter` say whether the
 * server can honestly apply that filter to the report: when it cannot, the filter bar does not offer it and says so, because the server would refuse it (`filter_not_supported`).
 */
export type ReportGroup = "Operational" | "Financial" | "Clinical" | "Measurement";

export interface ReportDef {
  id: string;
  label: string;
  group: ReportGroup;
  permission: string;
  description: string;
  providerFilter: boolean;
  locationFilter: boolean;
}

export const GROUPS: { group: ReportGroup; permission: string }[] = [
  { group: "Operational", permission: "ViewOperationalReports" },
  { group: "Financial", permission: "ViewFinancialReports" },
  { group: "Clinical", permission: "ViewClinicalReports" },
  { group: "Measurement", permission: "ViewMeasurementReports" },
];

export const REPORTS: ReportDef[] = [
  { id: "schedule", label: "Schedule", group: "Operational", permission: "ViewOperationalReports", providerFilter: true, locationFilter: true,
    description: "Appointments by day and by booked provider: how many are scheduled, cancelled or no-shows, and how much time is booked." },
  { id: "patient-flow", label: "Patient flow", group: "Operational", permission: "ViewOperationalReports", providerFilter: true, locationFilter: true,
    description: "Where each visit stands now, from scheduled to completed, and the moves that were recorded." },
  { id: "follow-ups", label: "Recall and follow-up", group: "Operational", permission: "ViewOperationalReports", providerFilter: false, locationFilter: false,
    description: "Tasks due in the period: how many were done, are still open, or are overdue." },
  { id: "cancellations", label: "Cancellations and no-shows", group: "Operational", permission: "ViewOperationalReports", providerFilter: true, locationFilter: true,
    description: "How many booked appointments were cancelled or missed, how early cancellations were made, by provider and by type." },
  { id: "production", label: "Production and charges", group: "Financial", permission: "ViewFinancialReports", providerFilter: true, locationFilter: false,
    description: "What was charged, less what was reversed, by provider and by procedure code, with the proof that it adds up to the ledger." },
  { id: "collections", label: "Collections and payments", group: "Financial", permission: "ViewFinancialReports", providerFilter: false, locationFilter: false,
    description: "Money received, less payments reversed and refunds, by payment method, with the proof that it adds up to the ledger." },
  { id: "balances", label: "Balances", group: "Financial", permission: "ViewFinancialReports", providerFilter: false, locationFilter: false,
    description: "What patients owed at the start and at the end of the period, and the change between them." },
  { id: "treatment-plans", label: "Treatment-plan status", group: "Clinical", permission: "ViewClinicalReports", providerFilter: false, locationFilter: false,
    description: "Plans made in the period and where each proposed procedure stands now: pending, accepted, declined or withdrawn." },
  { id: "completed-procedures", label: "Completed procedures", group: "Clinical", permission: "ViewClinicalReports", providerFilter: true, locationFilter: false,
    description: "Procedures performed in the period, by day, by the provider credited and by procedure code." },
  { id: "provider-activity", label: "Provider activity", group: "Clinical", permission: "ViewClinicalReports", providerFilter: true, locationFilter: false,
    description: "For each provider: appointments booked, visits finished and procedures completed. A visit seen by another provider counts for the one who saw the patient." },
];
