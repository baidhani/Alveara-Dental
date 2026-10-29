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
];
