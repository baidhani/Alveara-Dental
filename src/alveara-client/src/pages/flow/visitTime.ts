import type { Appointment } from "../../services/schedulingApi";

/** "just now", "12 min", "1 h 05 min", "2 h", "1 d 3 h", "12 d". Negative or tiny values read as "just now". From a day up it speaks in days so a board for another day stays readable. */
export function durationLabel(minutes: number): string {
  const m = Math.floor(minutes);
  if (m < 1) return "just now";
  if (m < 60) return `${m} min`;
  if (m >= 1440) {
    const d = Math.floor(m / 1440);
    const hours = Math.floor((m % 1440) / 60);
    return hours === 0 ? `${d} d` : `${d} d ${hours} h`;
  }
  const h = Math.floor(m / 60);
  const rest = m % 60;
  return rest === 0 ? `${h} h` : `${h} h ${String(rest).padStart(2, "0")} min`;
}

/**
 * The board's "now" in milliseconds: the SERVER's clock at the moment the board was fetched, moved forward by however long ago that was on this machine.
 * Only the elapsed time on the browser's clock is used, never its absolute time, so a wrong browser clock cannot make a wait look longer or shorter.
 */
export const boardNowMs = (serverNowUtc: string, fetchedAtClientMs: number, clientNowMs: number) =>
  Date.parse(serverNowUtc) + Math.max(0, clientNowMs - fetchedAtClientMs);

/**
 * The time cue on a card, or null when there is nothing honest to say. Before arrival: when the appointment starts (or how late it is). From check-in on: how long
 * the patient has been in the current step, measured from when the server recorded the move. Cancelled and no-show appointments have no cue.
 */
export function elapsedCue(a: Appointment, nowMs: number): string | null {
  if (a.status !== "Scheduled") return null;
  const flow = a.flowState ?? "Scheduled";
  if (flow === "Scheduled" || flow === "Confirmed") {
    const minutes = (Date.parse(a.startUtc) - nowMs) / 60000;
    return minutes >= 1 ? `Starts in ${durationLabel(minutes)}` : minutes > -1 ? "Starting now" : `${durationLabel(-minutes)} past the start time`;
  }
  if (!a.flowChangedAtUtc) return null;
  const minutes = (nowMs - Date.parse(a.flowChangedAtUtc)) / 60000;
  if (flow === "Completed") return minutes < 1 ? "Completed just now" : `Completed ${durationLabel(minutes)} ago`;
  return minutes < 1 ? "Just moved to this step" : `In this step ${durationLabel(minutes)}`;
}
