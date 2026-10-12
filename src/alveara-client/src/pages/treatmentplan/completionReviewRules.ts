/**
 * ALV-008-C01: what the completion review screen knows without asking the server: money in words, the time field's two forms, a count of what the selection will do (added up from the server's own readiness, never
 * worked out here), and the signature that decides whether a retry may reuse its save key. Pure functions only. The rules themselves (who can be completed, what the chart does, what is charged) are the
 * server's and arrive in the readiness; nothing here reimplements them.
 */
import type { ItemReadiness } from "../../services/completionApi";

export const money = (amount: number): string => `$${amount.toFixed(2)}`;

const pad = (n: number): string => String(n).padStart(2, "0");

/** A date and time as a `datetime-local` field shows it (the person's own clock, to the second so "now" is never earlier than something just recorded). */
export function localInputValue(moment: Date): string {
  return `${moment.getFullYear()}-${pad(moment.getMonth() + 1)}-${pad(moment.getDate())}T${pad(moment.getHours())}:${pad(moment.getMinutes())}:${pad(moment.getSeconds())}`;
}

/** The field's value as an instant in UTC for the request, or null when it is empty or not a date. */
export function toUtcIso(local: string): string | null {
  if (local.trim() === "") return null;
  const moment = new Date(local);
  return Number.isNaN(moment.getTime()) ? null : moment.toISOString();
}

export interface ReviewSummary {
  count: number;
  /** Procedures that will get a new charge, and what they add up to. */
  charges: number;
  chargeTotal: number;
  /** Procedures with a stored fee of zero: nothing is charged. */
  noCharge: number;
  /** Procedures whose one charge billing already holds. */
  alreadyCharged: number;
  chartMoves: number;
}

/** Adds up what the server said each selected procedure will do. */
export function summarize(items: ItemReadiness[], selected: ReadonlySet<string>): ReviewSummary {
  const chosen = items.filter((i) => selected.has(i.itemId));
  const billable = chosen.filter((i) => i.billable);
  const fresh = billable.filter((i) => !i.chargeExists);
  return {
    count: chosen.length,
    charges: fresh.length,
    chargeTotal: fresh.reduce((sum, i) => sum + i.fee, 0),
    noCharge: chosen.length - billable.length,
    alreadyCharged: billable.length - fresh.length,
    chartMoves: chosen.filter((i) => i.chartEffect === "Updated").length,
  };
}

export function summaryWords(s: ReviewSummary): string {
  if (s.count === 0) return "No procedure is selected.";
  const parts = [`${s.count} procedure${s.count === 1 ? "" : "s"} will be completed`];
  parts.push(s.charges === 0 ? "nothing new will be charged" : `${s.charges} charge${s.charges === 1 ? "" : "s"} totalling ${money(s.chargeTotal)} will be made`);
  if (s.alreadyCharged > 0) parts.push(`${s.alreadyCharged} already ${s.alreadyCharged === 1 ? "has its" : "have their"} charge`);
  if (s.noCharge > 0) parts.push(`${s.noCharge} ${s.noCharge === 1 ? "has" : "have"} no fee`);
  parts.push(s.chartMoves === 0 ? "no chart finding changes" : `${s.chartMoves} chart finding${s.chartMoves === 1 ? "" : "s"} will be marked Completed`);
  return `${parts.join("; ")}.`;
}

/** Everything that makes one request different from another. The same signature may reuse a save key (a retry); any change needs a new one. */
export function requestSignature(rowVersion: string, itemIds: string[], encounterId: string, providerId: string, performedAtUtc: string): string {
  return JSON.stringify([rowVersion, [...itemIds].sort(), encounterId, providerId, performedAtUtc]);
}

/** The first thing missing from the form, named beside its field before anything is sent. */
export function formGaps(providerId: string, encounterId: string, performedAtUtc: string | null, selectedCount: number): Record<string, string> {
  const gaps: Record<string, string> = {};
  if (selectedCount === 0) gaps.itemIds = "Choose at least one procedure to complete.";
  if (providerId === "") gaps.providerId = "Choose the provider who performed the care.";
  if (encounterId === "") gaps.encounterId = "Choose the encounter this care belongs to.";
  if (performedAtUtc === null) gaps.performedAtUtc = "Say when the procedures were performed.";
  return gaps;
}
