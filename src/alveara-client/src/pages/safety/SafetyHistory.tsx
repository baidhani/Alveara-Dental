import { useEffect, useState } from "react";
import { CHANGE_LABELS, getAlertHistory, getClearanceHistory } from "../../services/safetyApi";
import { when } from "./safetyText";
import "./Safety.css";

type Line = { key: number; what: string; text: string; reason: string | null; by: string | null; at: string };

/**
 * ALV-N011: the history of one alert or clearance - every version, oldest first, with who made it, when, what it said then and why. Loaded only when opened and again when the
 * subject's version changes. A change adds a version and never rewrites an earlier one, so this is also the record of any resolution and reopening.
 */
export function SafetyHistory({ kind, id, name, version }: { kind: "alert" | "clearance"; id: string; name: string; version: string }) {
  const [open, setOpen] = useState(false);
  const [lines, setLines] = useState<Line[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    const load = kind === "alert"
      ? getAlertHistory(id, controller.signal).then((h) => h.versions.map((v): Line => ({ key: v.versionNumber, what: CHANGE_LABELS[v.changeType], text: `${v.title} · ${v.severity} · ${v.status}${v.detail ? ` · ${v.detail}` : ""}`, reason: v.reason, by: v.actorName, at: v.occurredAtUtc })))
      : getClearanceHistory(id, controller.signal).then((h) => h.versions.map((v): Line => ({ key: v.versionNumber, what: CHANGE_LABELS[v.changeType], text: `${v.kind} · ${v.status}${v.documentReference ? ` · document ${v.documentReference}` : ""}`, reason: v.note, by: v.actorName, at: v.occurredAtUtc })));
    load.then((l) => { setFailed(false); setLines(l); }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [open, kind, id, version]);

  return (
    <details className="alv-clinical__history" onToggle={(e) => setOpen((e.currentTarget as HTMLDetailsElement).open)}>
      <summary aria-label={`History of ${name}`}>History</summary>
      {open && failed && <p className="alv-form-field__error" role="alert">Could not load the history. Close it and open it again.</p>}
      {open && !failed && lines === null && <p className="alv-clinical__meta">Loading the history…</p>}
      {open && lines && (
        <ol className="alv-clinical__history-list">
          {lines.map((l) => (
            <li key={l.key}>
              <span className="alv-clinical__history-what">{l.what}</span>
              <span> - {l.text}</span>
              {l.reason && <span> · Reason: {l.reason}</span>}
              <span className="alv-clinical__meta"> {when(l.at)} by {l.by ?? "a staff member"}</span>
            </li>
          ))}
        </ol>
      )}
    </details>
  );
}
