import { useEffect, useState } from "react";
import { CHANGE_LABELS, getToothHistory } from "../../services/odontogramApi";
import type { ToothEvent } from "../../services/odontogramApi";
import { when } from "./odontogramText";
import { SURFACE_NAMES } from "./toothNumbering";

/**
 * ALV-006-C01: everything that has happened to ONE tooth, oldest first: every finding ever recorded on it (withdrawn ones included) and every change to each, with who, when and why. It is
 * read from the append-only histories, so no later chart edit - a withdrawal, a re-entry, a retired condition - removes or re-words an event. Loaded only when opened, and again whenever
 * the tooth's findings change while it is open.
 */
export function ToothTimeline({ patientId, toothKey, version, toothLabel }: { patientId: string; toothKey: string; version: string; toothLabel: string }) {
  const [open, setOpen] = useState(false);
  const [events, setEvents] = useState<ToothEvent[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    getToothHistory(patientId, toothKey, controller.signal).then((h) => { setFailed(false); setEvents(h.events); }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [open, patientId, toothKey, version]);

  return (
    <details className="alv-clinical__history alv-odonto__timeline" onToggle={(e) => setOpen((e.currentTarget as HTMLDetailsElement).open)}>
      <summary aria-label={`History of tooth ${toothLabel}`}>Tooth history</summary>
      {open && failed && <p className="alv-form-field__error" role="alert">Could not load the tooth's history. Close it and open it again.</p>}
      {open && !failed && events === null && <p className="alv-clinical__meta">Loading the tooth's history…</p>}
      {open && events && events.length === 0 && <p className="alv-clinical__meta">Nothing has ever been recorded on this tooth.</p>}
      {open && events && events.length > 0 && (
        <ol className="alv-clinical__history-list" aria-label={`Everything recorded on tooth ${toothLabel}, oldest first`}>
          {events.map((e) => (
            <li key={`${e.findingId}-${e.versionNumber}`}>
              <span className="alv-clinical__history-what">{CHANGE_LABELS[e.changeType] ?? e.changeType}</span>
              <span> - {e.conditionLabel}{e.surface ? `, ${SURFACE_NAMES[e.surface]} surface` : ", whole tooth"} · {e.state}{e.status === "Withdrawn" ? " · withdrawn" : ""}</span>
              {e.reason && <span> · {e.changeType === "Linked" ? "Link" : "Reason"}: {e.reason}</span>}
              <span className="alv-clinical__meta"> {when(e.occurredAtUtc)} by {e.actorName ?? "Staff member"}</span>
            </li>
          ))}
        </ol>
      )}
    </details>
  );
}
