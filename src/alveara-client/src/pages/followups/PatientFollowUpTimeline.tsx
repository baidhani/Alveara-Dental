import { useEffect, useState } from "react";
import { getPatientTimeline } from "../../services/workQueueApi";
import type { TimelineEntry } from "../../services/workQueueApi";
import { actorWords, dueDetail, EVENT_LABEL, formatWhen } from "./followUpFormat";
import "./followups.css";

/**
 * ALV-009-C01: everything that happened to one patient's follow-ups, newest first: who did what, to which task, and when (the reminder and the tasks the system made or closed are the system's own entries). It reloads
 * whenever <paramref name="version"/> changes (the panel changes it after every action), says it could not load rather than showing an empty timeline, and shows no task text beyond the title.
 */
export function PatientFollowUpTimeline({ patientId, version }: { patientId: string; version: number }) {
  const [entries, setEntries] = useState<TimelineEntry[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getPatientTimeline(patientId, controller.signal).then((e) => { setEntries(e); setFailed(false); }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [patientId, version]);

  return (
    <section aria-labelledby={`alv-fu-timeline-${patientId}`} className="alv-fu-timeline">
      <h3 id={`alv-fu-timeline-${patientId}`}>Timeline</h3>
      {failed && <p role="alert" className="alv-fu-error">Could not load the timeline. Do not assume nothing happened.</p>}
      {!failed && entries === null && <p role="status">Loading the timeline…</p>}
      {entries !== null && entries.length === 0 && <p className="alv-clinical__meta">Nothing has happened to this patient&apos;s follow-ups yet.</p>}
      {entries !== null && entries.length > 0 && (
        <ol className="alv-fu-history" aria-label="Follow-up timeline">
          {entries.map((e, i) => (
            <li key={`${e.taskId}-${e.eventKind}-${e.occurredAtUtc}-${i}`}>
              <strong>{EVENT_LABEL[e.eventKind] ?? e.eventKind}</strong>: {e.taskTitle} by {actorWords(e.actorName, e.actorUserId)}, {formatWhen(e.occurredAtUtc)}
              {dueDetail(e.detail) ? ` (${dueDetail(e.detail)})` : ""}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
