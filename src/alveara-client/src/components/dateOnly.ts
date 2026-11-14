/**
 * ALV-N021 A1 (F-107): a date-only value from the server ("1985-03-14": a date of birth, a document date) shown in the viewer's own format WITHOUT passing through a time zone. `new Date("1985-03-14")`
 * is read as midnight UTC, which is the evening before in any zone behind UTC, so a date of birth printed one day early. The day is built from its parts in local time instead, so it is the same day
 * everywhere. A value that is not a date is shown as it came (never as "Invalid Date").
 */
export function formatDateOnly(isoDate: string, options?: Intl.DateTimeFormatOptions): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(isoDate);
  if (!match) return isoDate;
  const [y, m, d] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const day = new Date(y, m - 1, d);
  if (day.getFullYear() !== y || day.getMonth() !== m - 1 || day.getDate() !== d) return isoDate;
  return day.toLocaleDateString(undefined, options);
}

/**
 * ALV-N021 A1 (found in the A1 date-site classification, same kind as F-107): today's calendar date on the viewer's own clock, as a date box takes it ("YYYY-MM-DD"). `toISOString()` gives
 * the UTC day instead, which is tomorrow on a Chicago evening and yesterday on a morning east of UTC.
 */
export function todayDateOnly(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}
