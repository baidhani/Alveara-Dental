import { cancelCategoryLabel, dateOf, timeOf } from "../../services/schedulingApi";
import type { AppointmentEvent } from "../../services/schedulingApi";
import { STATE_LABELS } from "../flow/visitLabels";
import { showInstant } from "../../time/practiceTime";

const EVENT_LABELS: Record<string, string> = { Scheduled: "Scheduled", Rescheduled: "Rescheduled", Cancelled: "Cancelled", NoShow: "Marked as a no-show", NotesChanged: "Note changed",
  CheckedIn: "Patient checked in", TreatmentStarted: "Treatment started", Completed: "Visit completed",
  Confirmed: "Confirmed", Ready: "Marked ready", Seated: "Seated", CheckedOut: "Ready for checkout", AssignmentChanged: "Room or provider changed (earlier rule)",
  CheckInUndone: "Check-in undone", ProviderReassigned: "Provider changed", RoomChanged: "Room changed", KeptDespiteBlock: "Kept despite blocked time",
  CheckoutRecorded: "Checkout decision recorded",
};

/** Events whose stored detail only repeats what the "What" column already says (F-074), or that the category and reason below say better. */
const DETAIL_REPEATS = new Set(["Cancelled", "NoShow", "ProviderReassigned", "RoomChanged", "KeptDespiteBlock"]);

/**
 * ALV-N021 A2 (F-046, F-049, F-055, F-062, F-074): an appointment's history, oldest first. Each row says when, what, who did it and the details: a move says where it went from and to exactly
 * as the server recorded it (M4); an event recorded before M4 knows only where it came from and says only that, never guessing the rest. A visit move is said in words ("Scheduled to Checked
 * in"), and a detail that only repeats the event's name is left out.
 */
export function AppointmentHistory({ history }: { history: AppointmentEvent[] | null }) {
  return (
    <section aria-labelledby="cal-history-title">
      <h3 id="cal-history-title" className="alv-workspace__subtitle">History</h3>
      {history === null ? <p className="alv-workspace__note">Loading history…</p> : (
        <table className="alv-workspace__table">
          <caption className="alv-workspace__caption">What happened to this appointment, oldest first</caption>
          <thead><tr><th scope="col">When</th><th scope="col">What</th><th scope="col">By</th><th scope="col">Details</th></tr></thead>
          <tbody>
            {history.map((e, i) => (
              <tr key={`${e.eventType}-${i}`}>
                <td>{showInstant(e.occurredAtUtc)}</td>
                <td>{EVENT_LABELS[e.eventType] ?? e.eventType}</td>
                <td>{e.actorName ?? "—"}</td>
                <td>{describeEvent(e)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

const when = (local: string) => `${dateOf(local)} ${timeOf(local)}`;

/** A place as a sentence fragment: "2030-01-14 09:00 with Dr. Rivera in Op 1"; a missing part is left out rather than shown as a question mark. */
function place(start: string | null | undefined, provider: string | null | undefined, operatory: string | null | undefined): string {
  return [start ? when(start) : null, provider ? `with ${provider}` : null, operatory ? `in ${operatory}` : null].filter(Boolean).join(" ");
}

/** Where a move went: "from X to Y" when the destination was recorded, otherwise only where it came from (an event from before M4). */
function describeMove(e: AppointmentEvent): string | null {
  const hasFrom = !!(e.previousStartLocal || e.previousProviderName || e.previousOperatoryName);
  const hasTo = !!(e.newStartLocal || e.newProviderName || e.newOperatoryName);
  if (!hasTo) return hasFrom ? `Was ${place(e.previousStartLocal, e.previousProviderName, e.previousOperatoryName)}` : null;
  if (e.eventType === "Rescheduled") {
    return `From ${place(e.previousStartLocal, e.previousProviderName, e.previousOperatoryName)} to ${place(e.newStartLocal, e.newProviderName, e.newOperatoryName)}`;
  }
  const parts: string[] = [];
  if (e.newProviderName) parts.push(`Provider from ${e.previousProviderName ?? "not recorded"} to ${e.newProviderName}`);
  if (e.newOperatoryName) parts.push(`${parts.length ? "room" : "Room"} from ${e.previousOperatoryName ?? "not recorded"} to ${e.newOperatoryName}`);
  return parts.join("; ");
}

/** A visit move's stored "<from> -> <to>[ (N minutes late)]" in words; anything else is returned as it is. */
function humaniseFlow(detail: string): string {
  const m = /^(\w+) -> (\w+)(.*)$/.exec(detail);
  if (!m) return detail;
  const word = (s: string) => (STATE_LABELS as Record<string, string>)[s] ?? s;
  return `${word(m[1])} to ${word(m[2])}${m[3]}`;
}

/** One history row's details: from and to for a move, the visit move in words, the cancel category and the reason the person gave. */
function describeEvent(e: AppointmentEvent): string {
  const parts: string[] = [];
  const isMove = e.eventType === "Rescheduled" || e.eventType === "ProviderReassigned" || e.eventType === "RoomChanged";
  const move = isMove ? describeMove(e) : null;
  if (move) parts.push(move);
  else if (e.detail && !DETAIL_REPEATS.has(e.eventType)) parts.push(humaniseFlow(e.detail));
  if (e.category) parts.push(`Category: ${cancelCategoryLabel(e.category)}`);
  if (e.reason) parts.push(`Reason: ${e.reason}`);
  return parts.length > 0 ? parts.join(" · ") : "—";
}
