import type { ExportHistoryItem } from "../../services/patientExportsApi";

/** A moment in the practice's own time zone, with the zone named, so a line in the history reads the same wherever the screen is opened. */
export function formatMoment(iso: string, timeZone: string | null): string {
  const when = new Date(iso);
  const options: Intl.DateTimeFormatOptions = { year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit", timeZoneName: "short" };
  try {
    return when.toLocaleString(undefined, timeZone ? { ...options, timeZone } : options);
  } catch {
    return when.toLocaleString();                                                       // a time zone name this browser does not know: the browser's own zone is better than nothing
  }
}

/** A size in words, up to gigabytes (the limits of a package are gigabytes; the shared formatter stops at megabytes). */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}

export type ResultTone = "ok" | "warn" | "bad" | "wait";

/** How an export is described in words: a row still Started long after it began is Interrupted, a recent one is In progress. */
export function resultOf(item: ExportHistoryItem): { label: string; tone: ResultTone } {
  if (item.interrupted) return { label: "Interrupted", tone: "bad" };
  switch (item.result) {
    case "Started": return { label: "In progress", tone: "wait" };
    case "Complete": return { label: "Complete", tone: "ok" };
    case "Partial": return { label: "Partial", tone: "warn" };
    default: return { label: "Failed", tone: "bad" };
  }
}

/** "id" key of an excluded group turned into words ("accounts_and_credentials" becomes "Accounts and credentials"). */
export const groupTitle = (key: string) => key.replace(/_/g, " ").replace(/^./, (c) => c.toUpperCase());
