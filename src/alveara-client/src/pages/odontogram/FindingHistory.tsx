import { useEffect, useState } from "react";
import { CHANGE_LABELS, getFindingHistory } from "../../services/odontogramApi";
import type { FindingVersion } from "../../services/odontogramApi";
import { when } from "./odontogramText";
import { SURFACE_NAMES } from "./toothNumbering";

/**
 * STORY-006: the history of one finding - every version, oldest first, with who made it, when and why. Loaded only when opened and again when the finding's version changes. A change
 * adds a version and never rewrites an earlier one, so this is also the record of a withdrawal and the reason given.
 */
export function FindingHistory({ id, name, label, version }: { id: string; name: string; label: string; version: string }) {
  const [open, setOpen] = useState(false);
  const [versions, setVersions] = useState<FindingVersion[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    getFindingHistory(id, controller.signal).then((h) => { setFailed(false); setVersions(h.versions); }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [open, id, version]);

  return (
    <details className="alv-clinical__history" onToggle={(e) => setOpen((e.currentTarget as HTMLDetailsElement).open)}>
      <summary aria-label={`History of ${name}`}>History</summary>
      {open && failed && <p className="alv-form-field__error" role="alert">Could not load the history. Close it and open it again.</p>}
      {open && !failed && versions === null && <p className="alv-clinical__meta">Loading the history…</p>}
      {open && versions && (
        <ol className="alv-clinical__history-list">
          {versions.map((v) => (
            <li key={v.versionNumber}>
              <span className="alv-clinical__history-what">{CHANGE_LABELS[v.changeType] ?? v.changeType}</span>
              <span> - {label}{v.surface ? `, ${SURFACE_NAMES[v.surface]} surface` : ", whole tooth"} · {v.state}{v.status === "Withdrawn" ? " · withdrawn" : ""}</span>
              {v.reason && <span> · {v.changeType === "Linked" ? "Link" : "Reason"}: {v.reason}</span>}
              <span className="alv-clinical__meta"> {when(v.occurredAtUtc)} by {v.actorName ?? "Staff member"}</span>
            </li>
          ))}
        </ol>
      )}
    </details>
  );
}
