import { useState } from "react";
import { Button } from "../../components/Button";
import { SafeLink } from "../../components/SafeLink";
import { CATEGORY_LABELS, acknowledgeAlert, reopenAlert, resolveAlert, updateAlert } from "../../services/safetyApi";
import type { SafetyContext, SafetyEntry } from "../../services/safetyApi";
import { AlertForm, ReasonForm } from "./SafetyForms";
import type { SaveResult } from "./SafetyForms";
import { SafetyHistory } from "./SafetyHistory";
import { when } from "./safetyText";
import "./Safety.css";

type Mode = "view" | "edit" | "resolve" | "reopen";

interface Props {
  entry: SafetyEntry;
  patientId: string;
  canWrite: boolean;
  /** Anyone who can read the context may acknowledge (it records that they saw it and changes nothing else). */
  canAcknowledge: boolean;
  busy: boolean;
  run: (change: () => Promise<SafetyContext>, savedText: string) => Promise<SaveResult>;
}

/**
 * ALV-N011: one entry of the patient's safety picture. Its severity, kind and status are WORDS (colour is only a border), and it says where it came from and when it was last
 * updated and by whom. An entry read from the clinical record is shown read-only with a link to change it THERE (one source of truth). An alert can be acknowledged - which records
 * that YOU saw this revision and says plainly that it is still active - changed, resolved or (when resolved) reopened; resolving and reopening need a reason.
 */
export function SafetyEntryRow({ entry, patientId, canWrite, canAcknowledge, busy, run }: Props) {
  const [mode, setMode] = useState<Mode>("view");
  const isAlert = entry.origin === "Alert";
  const resolved = entry.status === "Resolved";
  const level = (entry.severity ?? "none").toLowerCase();

  return (
    <li className={`alv-safety__entry alv-safety__entry--${level}`}>
      <div className="alv-safety__entry-main">
        <p className="alv-safety__entry-title">
          {entry.title}{" "}
          {(entry.severity !== null || entry.category === "Allergy") && (
            <><span className={`alv-clinical__badge alv-safety__sev alv-safety__sev--${level}`}>{entry.severity ?? "Severity not recorded"}</span>{" "}</>
          )}
          <span className="alv-clinical__badge">{CATEGORY_LABELS[entry.category] ?? entry.category}</span>{" "}
          {isAlert && <span className={`alv-clinical__badge alv-clinical__badge--${resolved ? "finalized" : "draft"}`}>{resolved ? "Resolved" : "Active"}</span>}
        </p>
        {entry.detail && <p className="alv-clinical__entry-details">{entry.detail}</p>}
        <p className="alv-clinical__meta">Source: {entry.source} · Last updated {when(entry.lastUpdatedAtUtc)} by {entry.lastUpdatedByName ?? "a staff member"}</p>
        {entry.needsAttention && entry.attentionReason && <p className="alv-clinical__note" role="note">Needs review: {entry.attentionReason}</p>}
        {resolved && <p className="alv-clinical__meta">Resolved {entry.resolvedAtUtc ? when(entry.resolvedAtUtc) : ""} by {entry.resolvedByName ?? "a staff member"}: {entry.resolutionReason}</p>}

        {isAlert && !resolved && (
          entry.acknowledgedByMe ? (
            <p className="alv-clinical__meta">You acknowledged this{entry.acknowledgedAtUtc ? ` on ${when(entry.acknowledgedAtUtc)}` : ""}. It is still active - acknowledging does not resolve it.</p>
          ) : canAcknowledge && entry.revision !== null ? (
            <p className="alv-safety__ack">
              <Button type="button" onClick={() => void run(() => acknowledgeAlert(entry.id, entry.revision!), `${entry.title} acknowledged.`)} disabled={busy} aria-label={`Acknowledge: ${entry.title}`}>Acknowledge</Button>{" "}
              <span className="alv-clinical__meta">Records that you have seen this. It does not resolve it.</span>
            </p>
          ) : null
        )}
        {!isAlert && (
          <p className="alv-clinical__meta">
            Read from the clinical record. <SafeLink to={`/patients/${patientId}/clinical`} className="alv-workspace__link">Change it in the clinical record</SafeLink>.
          </p>
        )}
        {isAlert && entry.rowVersion && <SafetyHistory kind="alert" id={entry.id} name={entry.title} version={entry.rowVersion} />}

        {mode === "edit" && isAlert && entry.rowVersion && (
          <AlertForm
            initial={{ category: entry.category, title: entry.title, detail: entry.detail ?? "", severity: entry.severity ?? "", sourceNote: entry.source }}
            submitLabel="Save changes"
            busy={busy}
            onCancel={() => setMode("view")}
            onSubmit={async (input) => {
              const refused = await run(() => updateAlert(entry.id, { title: input.title, detail: input.detail, severity: input.severity, sourceNote: input.sourceNote }, entry.rowVersion!), "Alert changes saved.");
              if (!refused) setMode("view");
              return refused;
            }}
          />
        )}
        {mode === "resolve" && isAlert && entry.rowVersion && (
          <ReasonForm
            label={`Why is "${entry.title}" resolved?`}
            hint="The alert stays in the record, with who resolved it and this reason."
            required
            submitLabel="Resolve alert"
            ariaLabel={`Resolve alert: ${entry.title}`}
            busy={busy}
            onCancel={() => setMode("view")}
            onSubmit={async (reason) => {
              const refused = await run(() => resolveAlert(entry.id, reason, entry.rowVersion!), `${entry.title} resolved.`);
              if (!refused) setMode("view");
              return refused;
            }}
          />
        )}
        {mode === "reopen" && isAlert && entry.rowVersion && (
          <ReasonForm
            label={`Why is "${entry.title}" being reopened?`}
            required
            submitLabel="Reopen alert"
            ariaLabel={`Reopen alert: ${entry.title}`}
            busy={busy}
            onCancel={() => setMode("view")}
            onSubmit={async (reason) => {
              const refused = await run(() => reopenAlert(entry.id, reason, entry.rowVersion!), `${entry.title} reopened.`);
              if (!refused) setMode("view");
              return refused;
            }}
          />
        )}
      </div>
      {canWrite && isAlert && mode === "view" && (
        <div className="alv-clinical__row-actions">
          {!resolved && <Button type="button" onClick={() => setMode("edit")} disabled={busy} aria-label={`Change alert: ${entry.title}`}>Change</Button>}
          {!resolved && <Button type="button" onClick={() => setMode("resolve")} disabled={busy} aria-label={`Resolve alert: ${entry.title}`}>Resolve</Button>}
          {resolved && <Button type="button" onClick={() => setMode("reopen")} disabled={busy} aria-label={`Reopen alert: ${entry.title}`}>Reopen</Button>}
        </div>
      )}
    </li>
  );
}
