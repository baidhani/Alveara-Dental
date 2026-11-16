/** Text helpers for the Documents tab. Display only: every rule is enforced by the server. */
import { formatDateOnly } from "../../components/dateOnly";
import { sessionToday, showInstant } from "../../time/practiceTime";

export function formatBytes(bytes: number): string {
  const trim = (n: number) => String(Math.round(n * 10) / 10);
  if (bytes >= 1024 * 1024) return `${trim(bytes / (1024 * 1024))} MB`;
  if (bytes >= 1024) return `${trim(bytes / 1024)} KB`;
  return `${bytes} bytes`;
}

/** A date-only value from the server ("2030-03-09") shown in the viewer's own format, without letting the time zone move the day. */
export const formatDate = (isoDate: string): string => formatDateOnly(isoDate);

export const formatDateTime = (iso: string) => showInstant(iso);

/** ALV-N021 A3: the practice's today, as the value a date box takes; empty without a practice zone (never the browser's day). */
export function todayValue(now = new Date()): string {
  return sessionToday(now) ?? "";
}

/** The types the server will let the browser show; everything else is offered as a download only. */
export const INLINE_TYPES: ReadonlySet<string> = new Set(["pdf", "png", "jpeg"]);

/** Codes that are about the file itself, so their message is shown beside the file box. */
export const FILE_ERROR_CODES: ReadonlySet<string> = new Set([
  "file_empty", "file_too_large", "file_type_blocked", "file_type_mismatch", "executable_content", "file_name_invalid", "file_required",
]);

/** An encounter shown by the day it took place (the viewer's own date format). */
export const encounterLabel = (atUtc: string) => showInstant(atUtc, "date");

/** One line of a document's history in words. */
export function historyText(e: { kind: string; fromCategoryName: string | null; toCategoryName: string | null; byName: string; atUtc: string }): string {
  const when = formatDateTime(e.atUtc);
  switch (e.kind) {
    case "Imported": return `Imported by ${e.byName}, ${when}`;
    case "ImportedAsDuplicate": return `Imported as a duplicate by ${e.byName}, ${when}`;
    case "Categorized": return `Moved from ${e.fromCategoryName ?? "no category"} to ${e.toCategoryName ?? "no category"} by ${e.byName}, ${when}`;
    case "MetadataEdited": return `Details edited by ${e.byName}, ${when}`;
    case "Opened": return `Opened by ${e.byName}, ${when}`;
    case "Inactivated": return `Made inactive by ${e.byName}, ${when}`;
    case "Reactivated": return `Made active again by ${e.byName}, ${when}`;
    default: return `${e.kind} by ${e.byName}, ${when}`;
  }
}
