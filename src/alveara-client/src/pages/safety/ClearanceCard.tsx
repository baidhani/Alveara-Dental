import { useState } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { attachClearanceDocument, cancelClearance, receiveClearance, resolveClearance } from "../../services/safetyApi";
import type { Clearance, SafetyContext } from "../../services/safetyApi";
import { ReasonForm } from "./SafetyForms";
import type { SaveResult } from "./SafetyForms";
import { SafetyHistory } from "./SafetyHistory";
import { when } from "./safetyText";
import "./Safety.css";

type Mode = "view" | "receive" | "resolve" | "cancel" | "document";

const STATUS_WORDS: Record<Clearance["status"], string> = { Requested: "Requested - waiting", Received: "Received - not yet resolved", Resolved: "Resolved", Cancelled: "Cancelled" };

interface Props {
  clearance: Clearance;
  canWrite: boolean;
  busy: boolean;
  run: (change: () => Promise<SafetyContext>, savedText: string) => Promise<SaveResult>;
}

/**
 * ALV-N011: one clearance and its workflow. Requested -> Received -> Resolved (or Cancelled), each step with who, when and why. RECEIVED IS NOT RESOLVED: a received clearance stays
 * open and is said to be "not yet resolved" until someone resolves it with a reason; a clearance still waiting offers no resolve at all. The supporting document may not be
 * available yet - the clearance can be received without it, says "document not yet attached" in words, and the reference can be attached later (documents are a later story, so
 * the reference is just text).
 */
export function ClearanceCard({ clearance: c, canWrite, busy, run }: Props) {
  const [mode, setMode] = useState<Mode>("view");
  const label = `${c.kind} clearance: ${c.reason}`;
  const open = c.status === "Requested" || c.status === "Received";

  return (
    <li className="alv-safety__entry alv-safety__entry--clearance">
      <div className="alv-safety__entry-main">
        <p className="alv-safety__entry-title">
          {c.kind} clearance <span className={`alv-clinical__badge alv-clinical__badge--${open ? "draft" : "finalized"}`}>{STATUS_WORDS[c.status]}</span>
        </p>
        <p className="alv-clinical__entry-details">{c.reason}</p>
        <p className="alv-clinical__meta">
          Requested {when(c.requestedAtUtc)} by {c.requestedByName ?? "a staff member"}{c.requestedFrom ? ` from ${c.requestedFrom}` : ""}
          {c.receivedAtUtc ? ` · Received ${when(c.receivedAtUtc)} by ${c.receivedByName ?? "a staff member"}${c.receivedNote ? `: ${c.receivedNote}` : ""}` : ""}
        </p>
        {c.closedAtUtc && <p className="alv-clinical__meta">{c.status === "Resolved" ? "Resolved" : "Cancelled"} {when(c.closedAtUtc)} by {c.closedByName ?? "a staff member"}: {c.closingReason}</p>}
        {c.documentReference && <p className="alv-clinical__meta">Supporting document: {c.documentReference}</p>}
        {c.documentPending && <p className="alv-clinical__note" role="note">Supporting document not yet attached.{canWrite ? " Attach the reference when it arrives." : ""}</p>}
        <SafetyHistory kind="clearance" id={c.id} name={label} version={c.rowVersion} />

        {mode === "receive" && (
          <ReasonForm
            label="Note (optional)"
            hint="Leave the document blank if it has not arrived yet; you can attach it later."
            required={false}
            submitLabel="Mark as received"
            ariaLabel={`Receive ${label}`}
            busy={busy}
            onCancel={() => setMode("view")}
            onSubmit={async (note, extra) => {
              const refused = await run(() => receiveClearance(c.id, note, extra.documentReference ?? "", c.rowVersion), "Clearance marked as received.");
              if (!refused) setMode("view");
              return refused;
            }}
          >
            {(extra, set, errors) => <FormField label="Document reference (optional)" value={extra.documentReference ?? ""} error={errors.documentReference} maxLength={300} onChange={(e) => set("documentReference", e.target.value)} />}
          </ReasonForm>
        )}
        {mode === "document" && (
          <ReasonForm
            label="Note (optional)"
            required={false}
            submitLabel="Attach document"
            ariaLabel={`Attach document to ${label}`}
            busy={busy}
            onCancel={() => setMode("view")}
            onSubmit={async (_note, extra) => {
              const refused = await run(() => attachClearanceDocument(c.id, extra.documentReference ?? "", c.rowVersion), "Document attached.");
              if (!refused) setMode("view");
              return refused;
            }}
          >
            {(extra, set, errors) => <FormField label="Document reference" value={extra.documentReference ?? ""} error={errors.documentReference} maxLength={300} onChange={(e) => set("documentReference", e.target.value)} />}
          </ReasonForm>
        )}
        {mode === "resolve" && (
          <ReasonForm
            label="Why is this clearance resolved?"
            hint="For example: cleared for treatment with the physician's conditions. This is kept with who resolved it."
            required
            submitLabel="Resolve clearance"
            ariaLabel={`Resolve ${label}`}
            busy={busy}
            onCancel={() => setMode("view")}
            onSubmit={async (reason) => {
              const refused = await run(() => resolveClearance(c.id, reason, c.rowVersion), "Clearance resolved.");
              if (!refused) setMode("view");
              return refused;
            }}
          />
        )}
        {mode === "cancel" && (
          <ReasonForm
            label="Why is this clearance no longer needed?"
            required
            submitLabel="Cancel clearance"
            ariaLabel={`Cancel ${label}`}
            busy={busy}
            onCancel={() => setMode("view")}
            onSubmit={async (reason) => {
              const refused = await run(() => cancelClearance(c.id, reason, c.rowVersion), "Clearance cancelled.");
              if (!refused) setMode("view");
              return refused;
            }}
          />
        )}
      </div>
      {canWrite && mode === "view" && c.status !== "Cancelled" && (
        <div className="alv-clinical__row-actions">
          {c.status === "Requested" && <Button type="button" onClick={() => setMode("receive")} disabled={busy} aria-label={`Mark as received: ${label}`}>Mark as received</Button>}
          {c.status === "Received" && <Button type="button" onClick={() => setMode("resolve")} disabled={busy} aria-label={`Resolve: ${label}`}>Resolve</Button>}
          {(c.status === "Received" || c.status === "Resolved") && <Button type="button" onClick={() => setMode("document")} disabled={busy} aria-label={`${c.documentReference ? "Replace" : "Attach"} document: ${label}`}>{c.documentReference ? "Replace document" : "Attach document"}</Button>}
          {open && <Button type="button" onClick={() => setMode("cancel")} disabled={busy} aria-label={`Cancel: ${label}`}>Cancel clearance</Button>}
        </div>
      )}
    </li>
  );
}
