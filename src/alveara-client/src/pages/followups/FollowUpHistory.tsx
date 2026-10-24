import { useEffect, useState } from "react";
import { getFollowUp } from "../../services/followUpsApi";
import type { FollowUpEvent } from "../../services/followUpsApi";
import { formatWhen } from "./followUpFormat";

const LABEL: Record<string, string> = {
  Created: "Created", Edited: "Edited", Reassigned: "Reassigned", DueChanged: "Due date changed", Reminded: "Reminder sent",
  Completed: "Completed", Cancelled: "Cancelled", RecallCreated: "Next follow-up created",
};

/** The detail of an event worth showing in words: a due time. Other details are identifiers and are not shown. */
function detailOf(e: FollowUpEvent): string | null {
  const m = e.detail?.match(/^Due (.+)$/);
  return m && !Number.isNaN(new Date(m[1]).getTime()) ? `due ${formatWhen(m[1])}` : null;
}

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
          <strong>{LABEL[e.kind] ?? e.kind}</strong> by {e.actorName ?? (e.actorUserId ? "a colleague" : "the system")}, {formatWhen(e.occurredAtUtc)}
          {detailOf(e) ? ` (${detailOf(e)})` : ""}
        </li>
      ))}
    </ol>
  );
}
