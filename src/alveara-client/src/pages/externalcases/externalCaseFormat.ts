import type { CaseEvent, CaseKind, CaseStatus, PartyKind } from "../../services/externalCasesApi";

/** ALV-009-C02: words for the screens. Statuses, kinds and dates are always said in words; colour and badges only add to them. */
export const statusLabel = (status: CaseStatus | string): string => (status === "InProgress" ? "In progress" : status);

export const kindLabel = (kind: CaseKind): string => (kind === "Lab" ? "Lab case" : "Referral");

export const partyKindLabel = (kind: PartyKind): string => (kind === "LabVendor" ? "Lab vendor" : "Referral provider");

export const partyKindOf = (kind: CaseKind): PartyKind => (kind === "Lab" ? "LabVendor" : "ReferralProvider");

/** A practice-local calendar date (YYYY-MM-DD) shown without any time zone shifting. */
export function formatDay(day: string | null): string {
  if (!day) return "Not set";
  const [y, m, d] = day.split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
}

/** Today's date on this machine as YYYY-MM-DD (the browser runs in the practice's time zone). */
export function todayInput(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/** The move needs a written note: a cancellation always, and a lab case sent back from Received for a remake. */
export const noteRequired = (kind: CaseKind, from: CaseStatus, to: CaseStatus): boolean => to === "Cancelled" || (kind === "Lab" && from === "Received" && to === "Sent");

export const needsDate = (to: CaseStatus): boolean => to === "Sent" || to === "Received";

export function moveLabel(kind: CaseKind, from: CaseStatus, to: CaseStatus): string {
  if (to === "Sent") return from === "Received" ? "Send back for a remake" : kind === "Lab" ? "Mark as sent to the lab" : "Mark as sent";
  if (to === "InProgress") return "The lab has it (in progress)";
  if (to === "Received") return kind === "Lab" ? "Mark as received back" : "Report received";
  if (to === "Completed") return "Mark complete";
  return "Cancel the case";
}

export function describeEvent(e: Pick<CaseEvent, "kind" | "fromStatus" | "toStatus">): string {
  switch (e.kind) {
    case "Created": return "Created";
    case "Edited": return "Edited";
    case "StatusChanged": return `${statusLabel(e.fromStatus ?? "")} to ${statusLabel(e.toStatus ?? "")}`;
    case "DocumentLinked": return "Document linked";
    case "DocumentUnlinked": return "Document unlinked";
    default: return e.kind;
  }
}
