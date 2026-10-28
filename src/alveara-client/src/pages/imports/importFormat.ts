import type { ImportBatch, ImportKind } from "../../services/bulkImportApi";

export type Tone = "ok" | "warn" | "bad" | "wait";

const KIND_LABEL: Record<ImportKind, string> = { Patients: "Patients", Appointments: "Appointments", OpeningBalances: "Opening balances" };
export const kindLabel = (kind: string) => KIND_LABEL[kind as ImportKind] ?? kind;

/** What the last dry-run found valid: every row that is not otherwise settled (the server keeps these counters up to date as rows are decided and committed). */
export const validRows = (b: ImportBatch) => Math.max(0, b.rowCount - b.acceptedRows - b.rejectedRows - b.skippedRows - b.unresolvedRows);

/** "1 row", "2 rows". */
export const plural = (n: number, one: string, many = `${one}s`) => `${n} ${n === 1 ? one : many}`;

export const money = (amount: number) => amount.toLocaleString("en-US", { style: "currency", currency: "USD" });

/** The state of a batch in words (the colour only repeats what the words say). */
export function statusOf(b: ImportBatch): { label: string; tone: Tone } {
  switch (b.status) {
    case "Uploaded": return b.commitOutcome === "Refused" ? { label: "Commit refused, nothing written - run the dry-run again", tone: "bad" }
      : { label: b.mappingConfirmed ? "Ready for the dry-run" : "Needs its columns mapped", tone: "wait" };
    case "DryRunComplete": return b.commitOutcome === "Interrupted"
      ? { label: "Commit interrupted - can be confirmed again", tone: "bad" }
      : { label: "Dry-run done - ready to commit", tone: "wait" };
    case "Committing": return b.canRetry ? { label: "Commit stopped", tone: "bad" } : { label: "Committing", tone: "wait" };
    case "Committed": return b.commitOutcome === "CompletedWithExceptions" ? { label: "Committed with exceptions", tone: "warn" } : { label: "Committed", tone: "ok" };
    case "Failed": return { label: "Failed", tone: "bad" };
    default: return { label: "Cancelled", tone: "warn" };
  }
}

const REASON_WORDS: Record<string, string> = {
  invalid_fields: "A value in the row is not valid",
  already_registered: "Already registered",
  duplicate_in_file: "Repeats an earlier row of the file",
  possible_duplicate: "May be a patient who is already registered",
  reviewed_new_person: "A person decided this is a different person",
  reviewed_same_person: "A person decided this is the same person",
  already_scheduled: "Already on the schedule",
  patient_repeated_in_file: "An earlier row already carries a balance for this patient",
  account_not_empty: "The patient's account already has entries",
  start_in_past: "The start time has passed",
  provider_unavailable: "The provider is not working then",
  provider_double_booked: "The provider is booked then",
  operatory_conflict: "The room is booked then",
  patient_double_booked: "The patient is booked then",
};
export const reasonText = (reason: string | null) => (reason ? REASON_WORDS[reason] ?? reason.replace(/_/g, " ") : "");
