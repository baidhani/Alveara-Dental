import { useEffect, useState } from "react";
import { getFollowUp } from "../../services/followUpsApi";
import type { FollowUpEvent } from "../../services/followUpsApi";
import { actorWords, dueDetail, EVENT_LABEL, formatWhen } from "./followUpFormat";

/** STORY-009: one task's history, every action with who did it and when (the reminder is the system's own entry). */
export function FollowUpHistory({ taskId, version }: { taskId: string; version: string }) {
  const [events, setEvents] = useState<FollowUpEvent[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getFollowUp(taskId, controller.signal).then((d) => { setEvents(d.events); setFailed(false); }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [taskId, version]);

  if (failed) return <p role="alert" className="alv-fu-error">Could not load the history. Do not assume nothing happened.</p>;
  if (events === null) return <p role="status">Loading the history…</p>;
  return (
    <ol className="alv-fu-history" aria-label="History">
      {events.map((e, i) => (
        <li key={`${e.kind}-${e.occurredAtUtc}-${i}`}>
          <strong>{EVENT_LABEL[e.kind] ?? e.kind}</strong> by {actorWords(e.actorName, e.actorUserId)}, {formatWhen(e.occurredAtUtc)}
          {dueDetail(e.detail) ? ` (${dueDetail(e.detail)})` : ""}
        </li>
      ))}
    </ol>
  );
}
