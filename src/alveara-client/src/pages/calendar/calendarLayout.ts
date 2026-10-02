/**
 * ALV-004-C01: the calendar's pure date and layout logic. Everything works on practice-local wall-clock strings ("yyyy-MM-dd",
 * "yyyy-MM-ddTHH:mm") exactly as the API returns them - the browser's own time zone never enters into it - so a calendar opened from another
 * time zone still shows the practice's day. Date arithmetic is done on calendar days (UTC midnight of that date), which has no daylight-saving gaps.
 */
import type { Appointment } from "../../services/schedulingApi";

export const PIXELS_PER_MINUTE = 1;
export const MIN_BLOCK_PIXELS = 22;

const dayNumber = (day: string) => {
  const [y, m, d] = day.split("-").map(Number);
  return Date.UTC(y, m - 1, d) / 86_400_000;
};
const fromDayNumber = (n: number) => new Date(n * 86_400_000).toISOString().slice(0, 10);

export const addDays = (day: string, n: number) => fromDayNumber(dayNumber(day) + n);

/** 0 = Sunday ... 6 = Saturday, like .NET's DayOfWeek (which is what the availability windows use). */
export const dayOfWeek = (day: string) => new Date(dayNumber(day) * 86_400_000).getUTCDay();

export const DAY_NAMES = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
export const dayLabel = (day: string) => `${DAY_NAMES[dayOfWeek(day)]} ${day}`;

/** The Monday on or before a day (weeks start on Monday). */
export const weekStart = (day: string) => addDays(day, -((dayOfWeek(day) + 6) % 7));

/** The days a view shows: one day, or the seven days of the week containing it (Monday to Sunday). */
export function viewDays(day: string, view: "day" | "week"): string[] {
  if (view === "day") return [day];
  const monday = weekStart(day);
  return Array.from({ length: 7 }, (_, i) => addDays(monday, i));
}

/** Minutes since midnight of a "yyyy-MM-ddTHH:mm" (or "HH:mm") string. */
export function minutesOf(timeOrLocal: string): number {
  const time = timeOrLocal.includes("T") ? timeOrLocal.slice(11, 16) : timeOrLocal;
  const [h, m] = time.split(":").map(Number);
  return h * 60 + m;
}

export const hhmm = (minutes: number) => `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}`;

/** The hours the grid covers: 07:00-19:00 by default, widened (to whole hours) to include any appointment or working window outside that. */
export function axisRange(appointments: Appointment[], windows: { startLocal: string; endLocal: string }[]): { startMinute: number; endMinute: number } {
  let start = 7 * 60;
  let end = 19 * 60;
  for (const a of appointments) {
    start = Math.min(start, minutesOf(a.startLocal));
    // an appointment ending on a later day (or at midnight) is clipped to the day by the block layout; here only same-day ends matter
    const endMinute = dateOf(a.endLocal) === dateOf(a.startLocal) ? minutesOf(a.endLocal) : 24 * 60;
    end = Math.max(end, endMinute);
  }
  for (const w of windows) {
    start = Math.min(start, minutesOf(w.startLocal));
    end = Math.max(end, minutesOf(w.endLocal));
  }
  return { startMinute: Math.floor(start / 60) * 60, endMinute: Math.min(24 * 60, Math.ceil(end / 60) * 60) };
}

const dateOf = (local: string) => local.slice(0, 10);

export interface Placed {
  appointment: Appointment;
  topPx: number;
  heightPx: number;
  /** Which side-by-side lane the block sits in, out of `lanes` for its overlapping group. */
  lane: number;
  lanes: number;
}

/**
 * Places one column's appointments: vertical position and height from the time, and - when appointments overlap in time (several providers in one
 * day column of the week view, or a cancelled appointment shown over the time it used to hold) - side-by-side lanes so nothing hides anything else.
 * Appointments are grouped into clusters of transitively overlapping blocks; every block in a cluster gets the same lane count.
 */
export function placeColumn(appointments: Appointment[], day: string, axisStartMinute: number): Placed[] {
  const items = appointments
    .map((a) => {
      const start = dateOf(a.startLocal) === day ? minutesOf(a.startLocal) : 0;
      const end = dateOf(a.endLocal) === day ? minutesOf(a.endLocal) : 24 * 60;
      return { a, start, end: Math.max(end, start + 1) };
    })
    .sort((x, y) => x.start - y.start || x.end - y.end || x.a.id.localeCompare(y.a.id));

  const placed: Placed[] = [];
  let cluster: typeof items = [];
  let clusterEnd = -1;
  const flush = () => {
    if (cluster.length === 0) return;
    const laneEnds: number[] = [];
    const assigned = cluster.map((it) => {
      let lane = laneEnds.findIndex((e) => e <= it.start);
      if (lane === -1) lane = laneEnds.length;
      laneEnds[lane] = it.end;
      return { it, lane };
    });
    for (const { it, lane } of assigned) {
      placed.push({
        appointment: it.a,
        topPx: (it.start - axisStartMinute) * PIXELS_PER_MINUTE,
        heightPx: Math.max((it.end - it.start) * PIXELS_PER_MINUTE, MIN_BLOCK_PIXELS),
        lane,
        lanes: laneEnds.length,
      });
    }
    cluster = [];
    clusterEnd = -1;
  };
  for (const it of items) {
    if (cluster.length > 0 && it.start >= clusterEnd) flush();
    cluster.push(it);
    clusterEnd = Math.max(clusterEnd, it.end);
  }
  flush();
  return placed;
}

/** What a block says, in words (never colour alone): the status is spelled out for anything that is not simply scheduled. */
export const statusWord = (status: string) => (status === "Cancelled" ? "Cancelled" : status === "NoShow" ? "No-show" : "Scheduled");

/**
 * STORY-011: the flow of a scheduled appointment in words, or null while the patient has simply not arrived yet (nothing to say). Never colour alone.
 */
export const flowWord = (a: { status: string; flowState?: string }): string | null =>
  a.status !== "Scheduled" ? null : a.flowState === "CheckedIn" ? "Checked in" : a.flowState === "InTreatment" ? "In treatment" : a.flowState === "Completed" ? "Completed" : null;

/** Which appointments belong in a day column. An appointment is in the column of every day it touches. */
export const appointmentsOnDay = (appointments: Appointment[], day: string) =>
  appointments.filter((a) => dateOf(a.startLocal) <= day && day <= dateOf(a.endLocal) && !(dateOf(a.endLocal) === day && minutesOf(a.endLocal) === 0 && dateOf(a.startLocal) !== day));

/** "now" in the practice's own time zone as "yyyy-MM-ddTHH:mm", from the zone id the scheduling snapshot reports. */
export function practiceNow(timeZoneId: string, now: Date = new Date()): string {
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone: timeZoneId, year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hourCycle: "h23",
  }).formatToParts(now);
  const get = (t: string) => parts.find((p) => p.type === t)!.value;
  return `${get("year")}-${get("month")}-${get("day")}T${get("hour")}:${get("minute")}`;
}

/** The start minute of a click at `offsetY` pixels down a column, snapped down to a 15-minute slot. */
export const slotMinuteAt = (offsetY: number, axisStartMinute: number) => axisStartMinute + Math.floor(offsetY / PIXELS_PER_MINUTE / 15) * 15;
