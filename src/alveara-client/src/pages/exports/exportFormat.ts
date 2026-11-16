import type { ExportHistoryItem } from "../../services/patientExportsApi";
import { formatInstant, TIME_UNAVAILABLE } from "../../time/practiceTime";

/**
 * A moment in the practice's own time zone, with the zone named, so a line in the history reads the same wherever the screen is opened.
 * ALV-N021 A3: rebuilt on the shared helper; without a usable zone it says "Time unavailable" - it no longer falls back to the browser's zone.
 */
export function formatMoment(iso: string, timeZone: string | null): string {
  return formatInstant(iso, timeZone, "moment") ?? TIME_UNAVAILABLE;
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
