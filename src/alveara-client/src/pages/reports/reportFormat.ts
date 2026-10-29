import type { ReportQuery } from "../../services/reportsApi";

/** Dates are practice-local days written year-month-day. The hub uses the browser's own calendar for "today" and the presets; the server reads them in the practice's time zone. */
const pad = (n: number) => String(n).padStart(2, "0");
export const isoDate = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
export const today = () => isoDate(new Date());

export function addDays(iso: string, days: number): string {
  const [y, m, d] = iso.split("-").map(Number);
  return isoDate(new Date(y, m - 1, d + days));
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
      const now = new Date();
      const first = new Date(now.getFullYear(), now.getMonth() - 1, 1);
      const last = new Date(now.getFullYear(), now.getMonth(), 0);
      return { from: isoDate(first), to: isoDate(last) };
    } },
];

/** A rate is a fraction of the records counted; null is "not available" (nothing was counted), never zero. */
export const percent = (rate: number | null) => (rate === null ? "Not available" : `${(rate * 100).toFixed(1)}%`);

export const hoursAndMinutes = (minutes: number) => `${Math.floor(minutes / 60)} h ${minutes % 60} min`;

/** "CheckedIn" -> "Checked in". */
export const words = (s: string) => s.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, (c) => c.toUpperCase()).replace(/ ([A-Z])/g, (m) => m.toLowerCase());
