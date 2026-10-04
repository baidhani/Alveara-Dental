import { useEffect, useState } from "react";
import { CHANGE_LABELS, getItemHistory } from "../../../services/clinicalRecordApi";
import type { ItemHistory as History, ItemVersion } from "../../../services/clinicalRecordApi";
import "../Clinical.css";

const when = (iso: string) => new Date(iso).toLocaleString();

/** What a version says, as a clinician reads it: the status first, then whichever details were recorded. */
function summary(v: ItemVersion): string {
  return [`Status ${v.status}`, v.reaction && `Reaction: ${v.reaction}`, v.severity && `Severity: ${v.severity}`, v.dose && `Dose: ${v.dose}`, v.frequency && `Frequency: ${v.frequency}`, v.detail].filter(Boolean).join(" · ");
}

/**
 * ALV-005-C01: the history of one item - every version it has had, oldest first, with who changed it, when, what the item said then and why (when the clinician said).
 * Loaded only when it is opened. A change adds a version and never rewrites an earlier one, so this is also the record of any correction. It is reloaded when the item's
 * version changes, so a change made while it is open appears in it.
 */
export function ItemHistory({ itemId, itemName, version }: { itemId: string; itemName: string; version: string }) {
  const [open, setOpen] = useState(false);
  const [history, setHistory] = useState<History | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    getItemHistory(itemId, controller.signal)
      .then((h) => { setFailed(false); setHistory(h); })
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true);
      });
    return () => controller.abort();
  }, [open, itemId, version]);

  return (
    <details className="alv-clinical__history" onToggle={(e) => setOpen((e.currentTarget as HTMLDetailsElement).open)}>
      <summary aria-label={`History of ${itemName}`}>History</summary>
      {open && failed && <p className="alv-form-field__error" role="alert">Could not load the history. Close it and open it again.</p>}
      {open && !failed && history === null && <p className="alv-clinical__meta">Loading the history…</p>}
      {open && history && (
        <ol className="alv-clinical__history-list">
          {history.versions.map((v) => (
            <li key={v.versionNumber}>
              <span className="alv-clinical__history-what">{CHANGE_LABELS[v.changeType]}</span>
              <span> - {v.name} · {summary(v)}</span>
              {v.reason && <span> · Reason: {v.reason}</span>}
              <span className="alv-clinical__meta"> {when(v.occurredAtUtc)} by {v.actorName ?? "a staff member"}</span>
            </li>
          ))}
        </ol>
      )}
    </details>
  );
}
