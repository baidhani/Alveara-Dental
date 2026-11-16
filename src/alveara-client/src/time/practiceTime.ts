/**
 * ALV-N021 A3 (briefing sections 4.1 and 6): the ONLY client code that turns an instant into practice time or derives a practice calendar day.
 *
 * The server owns the practice time zone (an IANA id in the session response, `practiceTimeZoneId`), typed-time conversion and every stored value; these
 * helpers only RENDER a server instant in that server-supplied zone. They never fall back to the browser's zone: without a valid zone (or with an
 * unparsable instant) every helper returns `null`, and the screen shows "Time unavailable" instead of a misleading browser-local time.
 *
 * - `formatInstant`: readable display text; may follow the viewer's locale. Never used to fill a `datetime-local` control.
 * - `toPracticeLocalInput`: the exact ASCII value of a `datetime-local` control (`yyyy-MM-ddTHH:mm`, `:ss` only on request) for a prefill; Latin digits,
 *   Gregorian calendar, 24-hour clock, independent of the browser's locale, calendar and zone. What the person then types is sent as plain text
 *   (`...Local`) and is never turned into an instant here.
 * - `practiceToday` / `practiceNow`: the practice calendar day `yyyy-MM-dd` / wall time `yyyy-MM-ddTHH:mm`, built the same exact way.
 *
 * Date-only values (date of birth, service dates) are not instants: they use `formatDateOnly` (components/dateOnly.ts) and are never zone-converted.
 */

/** The display styles the screens use today. */
export type InstantStyle = "date-time" | "date" | "time" | "time-seconds" | "moment";

const STYLES: Record<InstantStyle, Intl.DateTimeFormatOptions> = {
  "date-time": { year: "numeric", month: "numeric", day: "numeric", hour: "numeric", minute: "2-digit", second: "2-digit" },
  date: { year: "numeric", month: "numeric", day: "numeric" },
  time: { hour: "numeric", minute: "2-digit" },
  "time-seconds": { hour: "2-digit", minute: "2-digit", second: "2-digit" },
  // reports and export history: short month and the zone named
  moment: { year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit", timeZoneName: "short" },
};

/** The fixed locale for exact machine text: Latin digits, Gregorian calendar (the locale's own calendar and digits are never used). */
const EXACT_LOCALE = "en-US-u-nu-latn-ca-gregory";

/** True when `zone` is an id this browser's Intl accepts (an IANA id from the server). */
export function isUsableZone(zone: string | null | undefined): zone is string {
  if (!zone) return false;
  try {
    new Intl.DateTimeFormat(EXACT_LOCALE, { timeZone: zone });
    return true;
  } catch {
    return false;   // RangeError: the browser does not know this zone - treated as unavailable, never replaced by the browser zone
  }
}

function instantOf(value: string | number | Date): Date | null {
  const d = value instanceof Date ? value : new Date(value);
  return Number.isNaN(d.getTime()) ? null : d;
}

/** Readable text for an instant in the practice zone, or null without a usable zone or a valid instant. */
export function formatInstant(utc: string | number | Date | null | undefined, zone: string | null | undefined, style: InstantStyle = "date-time", locale?: string): string | null {
  if (utc === null || utc === undefined || !isUsableZone(zone)) return null;
  const d = instantOf(utc);
  if (!d) return null;
  return new Intl.DateTimeFormat(locale, { ...STYLES[style], timeZone: zone }).format(d);
}

/** The practice wall-clock parts of an instant, exactly (Latin digits, Gregorian, h23). */
function exactParts(d: Date, zone: string): Record<"year" | "month" | "day" | "hour" | "minute" | "second", string> {
  const parts = new Intl.DateTimeFormat(EXACT_LOCALE, {
    timeZone: zone, hourCycle: "h23", year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit",
  }).formatToParts(d);
  const get = (t: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === t)?.value ?? "";
  return { year: get("year").padStart(4, "0"), month: get("month"), day: get("day"), hour: get("hour"), minute: get("minute"), second: get("second") };
}

/** The exact value of a `datetime-local` control for an instant: "yyyy-MM-ddTHH:mm" (or with ":ss"), or null without a usable zone or a valid instant. */
export function toPracticeLocalInput(utc: string | number | Date | null | undefined, zone: string | null | undefined, options: { seconds?: boolean } = {}): string | null {
  if (utc === null || utc === undefined || !isUsableZone(zone)) return null;
  const d = instantOf(utc);
  if (!d) return null;
  const p = exactParts(d, zone);
  return `${p.year}-${p.month}-${p.day}T${p.hour}:${p.minute}${options.seconds ? `:${p.second}` : ""}`;
}

/** The practice wall time now, "yyyy-MM-ddTHH:mm", or null without a usable zone. */
export function practiceNow(zone: string | null | undefined, now: Date = new Date()): string | null {
  return toPracticeLocalInput(now, zone);
}

/** The practice calendar day now, "yyyy-MM-dd", or null without a usable zone. */
export function practiceToday(zone: string | null | undefined, now: Date = new Date()): string | null {
  return practiceNow(zone, now)?.slice(0, 10) ?? null;
}

/** Date-only arithmetic on a "yyyy-MM-dd" day (no zone involved): the day `days` later (negative for earlier). */
export function addDaysToDate(day: string, days: number): string {
  const [y, m, d] = day.split("-").map(Number);
  const t = new Date(Date.UTC(y, m - 1, d + days));
  return `${String(t.getUTCFullYear()).padStart(4, "0")}-${String(t.getUTCMonth() + 1).padStart(2, "0")}-${String(t.getUTCDate()).padStart(2, "0")}`;
}

/** What a screen shows in place of a time it cannot render. */
export const TIME_UNAVAILABLE = "Time unavailable";

/**
 * The practice zone the screens render in: set ONLY by the AuthProvider from the newest authenticated session response (and cleared when the session
 * ends), so plain formatting helpers outside React can use it without each caller passing the zone. Null means "unavailable" - never the browser zone.
 */
let renderZone: string | null = null;

/** Called by the AuthProvider (and the test setup); not for screens. */
export function setRenderZone(zone: string | null): void {
  renderZone = isUsableZone(zone) ? zone : null;
}

/** The zone the screens currently render in, or null. */
export const getRenderZone = (): string | null => renderZone;

/** Readable text for an instant in the session's practice zone, or "Time unavailable": what every screen shows for a server instant. */
export function showInstant(utc: string | number | Date, style: InstantStyle = "date-time"): string {
  return formatInstant(utc, renderZone, style) ?? TIME_UNAVAILABLE;
}
