import type { ReportQuery } from "../../services/reportsApi";
import { addDaysToDate, sessionToday } from "../../time/practiceTime";

/**
 * Dates are practice-local days written year-month-day. ALV-N021 A3: "today" and the presets come from the practice's today (the session zone), never
 * the browser's calendar; the arithmetic on them is date-only. Without a practice zone they are empty and the server refuses the range.
 */
export const today = () => sessionToday() ?? "";

export function addDays(iso: string, days: number): string {
  return iso === "" ? "" : addDaysToDate(iso, days);
}

const DATE = /^\d{4}-\d{2}-\d{2}$/;
export const isDate = (s: string | null | undefined): s is string => !!s && DATE.test(s);

export const defaultQuery = (): ReportQuery => ({ from: addDays(today(), -29), to: today(), providerIds: [], locationId: null });

export function queryFromSearch(p: URLSearchParams): ReportQuery {
  const fallback = defaultQuery();
  const from = p.get("from");
  const to = p.get("to");
  return { from: isDate(from) ? from : fallback.from, to: isDate(to) ? to : fallback.to, providerIds: p.getAll("providerIds"), locationId: p.get("locationId") || null };
}

export function searchFromQuery(q: ReportQuery): URLSearchParams {
  const p = new URLSearchParams({ from: q.from, to: q.to });
  for (const id of q.providerIds) p.append("providerIds", id);
  if (q.locationId) p.set("locationId", q.locationId);
  return p;
}

export const queryKey = (q: ReportQuery) => `${q.from}|${q.to}|${[...q.providerIds].sort().join(",")}|${q.locationId ?? ""}`;

export const PRESETS: { label: string; range: () => { from: string; to: string } }[] = [
  { label: "Last 7 days", range: () => ({ from: addDays(today(), -6), to: today() }) },
  { label: "Last 30 days", range: () => ({ from: addDays(today(), -29), to: today() }) },
  { label: "This month", range: () => ({ from: `${today().slice(0, 8)}01`, to: today() }) },
  { label: "Last month", range: () => {
      const t = today();
      if (t === "") return { from: "", to: "" };
      const last = addDaysToDate(`${t.slice(0, 8)}01`, -1);   // the day before the first of this month
      return { from: `${last.slice(0, 8)}01`, to: last };
    } },
];

/** A rate is a fraction of the records counted; null is "not available" (nothing was counted), never zero. */
export const percent = (rate: number | null) => (rate === null ? "Not available" : `${(rate * 100).toFixed(1)}%`);

/** Dollars and cents, with a minus sign for a negative amount (a payment's effect on what is owed). */
export const money = (amount: number) => new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(amount);

export const hoursAndMinutes = (minutes: number) => `${Math.floor(minutes / 60)} h ${minutes % 60} min`;

/** "CheckedIn" -> "Checked in". */
export const words = (s: string) => s.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, (c) => c.toUpperCase()).replace(/ ([A-Z])/g, (m) => m.toLowerCase());

/** The ledger lines in words, for the financial reports. */
export const LINE_LABELS: Record<string, string> = {
  Charges: "Charges",
  ChargeReversals: "Charges reversed",
  Payments: "Payments received",
  PaymentReversals: "Payments reversed",
  Refunds: "Refunds",
  RefundReversals: "Refunds reversed",
  Adjustments: "Adjustments",
  AdjustmentReversals: "Adjustments reversed",
  OpeningBalances: "Opening balances loaded by an import",
  Unclassified: "Not classified",
};
