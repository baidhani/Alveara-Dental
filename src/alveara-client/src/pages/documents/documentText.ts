/** Text helpers for the Documents tab. Display only: every rule is enforced by the server. */

export function formatBytes(bytes: number): string {
  const trim = (n: number) => String(Math.round(n * 10) / 10);
  if (bytes >= 1024 * 1024) return `${trim(bytes / (1024 * 1024))} MB`;
  if (bytes >= 1024) return `${trim(bytes / 1024)} KB`;
  return `${bytes} bytes`;
}

/** A date-only value from the server ("2030-03-09") shown in the viewer's own format, without letting the time zone move the day. */
export function formatDate(isoDate: string): string {
  const [y, m, d] = isoDate.slice(0, 10).split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString();
}

export const formatDateTime = (iso: string) => new Date(iso).toLocaleString();

/** Today's date in the viewer's own calendar, as the value a date box takes. */
export function todayValue(now = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** The types the server will let the browser show; everything else is offered as a download only. */
export const INLINE_TYPES: ReadonlySet<string> = new Set(["pdf", "png", "jpeg"]);

/** Codes that are about the file itself, so their message is shown beside the file box. */
export const FILE_ERROR_CODES: ReadonlySet<string> = new Set([
  "file_empty", "file_too_large", "file_type_blocked", "file_type_mismatch", "executable_content", "file_name_invalid", "file_required",
]);
