import { getBalanceAccounts, getLedgerEntries, getPlanItems, getProcedures } from "../../services/reportsApi";
import type { BalanceAccountRecord, BalanceCursor, ClinicalCursor, LedgerCursor, LedgerDrill, LedgerEntryRecord, PlanItemRecord, ProcedureDrill, ProcedureRecord, ReportQuery } from "../../services/reportsApi";
import { formatMoment } from "../exports/exportFormat";
import { PatientLink, RecordsPanel } from "./RecordsPanel";
import { money, words } from "./reportFormat";

interface Common { query: ReportQuery; title: string; timeZone: string; onClose: () => void }

/** The ledger entries behind a financial number. The amount is the entry as stored: its effect on what the patient owes (a payment is negative). */
export function LedgerEntryRecords({ query, drill, title, timeZone, onClose }: Common & { drill: LedgerDrill }) {
  return (
    <RecordsPanel<LedgerEntryRecord, LedgerCursor>
      title={title} onClose={onClose} rowKey={(r) => r.id}
      load={(after, signal) => getLedgerEntries(query, { ...drill, after }, signal)}
      columns={[
        { header: "Recorded", cell: (r) => formatMoment(r.recordedAtUtc, timeZone) },
        { header: "Entry", cell: (r) => `No. ${r.entryNumber}` },
        { header: "Kind", cell: (r) => words(r.kind) },
        { header: "Effect on what is owed", cell: (r) => money(r.amount) },
        { header: "Method", cell: (r) => r.paymentMethod ?? "None" },
        { header: "Procedure code", cell: (r) => r.procedureCode ?? "None" },
        { header: "Patient", cell: (r) => <PatientLink id={r.patientId} /> },
      ]}
    />
  );
}

/** The accounts behind a balance number, largest first. The balance is what the account owes (negative would be a credit). */
export function BalanceAccountRecords({ query, side, title, onClose }: Omit<Common, "timeZone"> & { side: string }) {
  return (
    <RecordsPanel<BalanceAccountRecord, BalanceCursor>
      title={title} onClose={onClose} rowKey={(r) => r.accountId}
      load={(after, signal) => getBalanceAccounts(query, { side, after }, signal)}
      columns={[
        { header: "Balance", cell: (r) => money(r.balance) },
        { header: "Patient", cell: (r) => <PatientLink id={r.patientId} /> },
      ]}
    />
  );
}

/** The plan items behind a treatment-plan cell. The fee is the practice's estimate in the plan, not an amount billed. */
export function PlanItemRecords({ query, cell, title, onClose }: Omit<Common, "timeZone"> & { cell?: string }) {
  return (
    <RecordsPanel<PlanItemRecord, ClinicalCursor>
      title={title} onClose={onClose} rowKey={(r) => r.itemId}
      load={(after, signal) => getPlanItems(query, { cell, after }, signal)}
      columns={[
        { header: "Item", cell: (r) => `No. ${r.itemNumber}` },
        { header: "Decision", cell: (r) => (r.withdrawn ? "Withdrawn" : words(r.acceptance)) },
        { header: "Scheduling", cell: (r) => words(r.scheduling) },
        { header: "Care", cell: (r) => words(r.completion) },
        { header: "Estimated fee", cell: (r) => money(r.estimatedFee) },
        { header: "Patient", cell: (r) => <PatientLink id={r.patientId} /> },
      ]}
    />
  );
}

/** The completed procedures behind a number: when it was performed (as corrected, if it was), its catalog code and the plan's fee estimate. */
export function ProcedureRecords({ query, drill, title, timeZone, onClose }: Common & { drill: ProcedureDrill }) {
  return (
    <RecordsPanel<ProcedureRecord, ClinicalCursor>
      title={title} onClose={onClose} rowKey={(r) => r.id}
      load={(after, signal) => getProcedures(query, { ...drill, after }, signal)}
      columns={[
        { header: "Performed", cell: (r) => formatMoment(r.performedAtUtc, timeZone) },
        { header: "Code", cell: (r) => `${r.codeSystem} ${r.code}` },
        { header: "Estimated fee", cell: (r) => money(r.estimatedFee) },
        { header: "Patient", cell: (r) => <PatientLink id={r.patientId} /> },
      ]}
    />
  );
}
