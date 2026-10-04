import { useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../../components/Button";
import { entryInputOf } from "../../../services/clinicalApi";
import { removeRecordItem, setItemStatus, statusesFor, updateRecordItem } from "../../../services/clinicalRecordApi";
import type { ClinicalRecord, ItemStatus, RecordItem } from "../../../services/clinicalRecordApi";
import { EntryForm } from "../EntryForm";
import type { SaveResult } from "../SectionPanel";
import { ItemHistory } from "./ItemHistory";
import "../Clinical.css";

const when = (iso: string) => new Date(iso).toLocaleDateString();

interface Props {
  item: RecordItem;
  canWrite: boolean;
  busy: boolean;
  run: (change: () => Promise<ClinicalRecord>, savedText: string) => Promise<SaveResult>;
}

type Mode = "view" | "edit" | "status" | "remove";

/** The details of one item the way a clinician reads them (reaction and severity for an allergy, dose and frequency for a medication). */
function details(item: RecordItem): string {
  return [item.reaction && `Reaction: ${item.reaction}`, item.severity && `Severity: ${item.severity}`, item.dose && `Dose: ${item.dose}`, item.frequency && `Frequency: ${item.frequency}`, item.detail]
    .filter(Boolean).join(" · ");
}

/** A small form with one required or optional reason and a confirm button; typed text stays until the server accepts it. */
function ReasonForm({ label, hint, required, submitLabel, busy, onSubmit, onCancel, children }: {
  label: string; hint?: string; required: boolean; submitLabel: string; busy: boolean; onSubmit: (reason: string) => Promise<SaveResult>; onCancel: () => void; children?: React.ReactNode;
}) {
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const id = useId();
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    if (required && reason.trim() === "") {
      setError("Say why.");
      return;
    }
    setError(null);
    const refused = await onSubmit(reason.trim());
    if (refused) setError(refused.reason ?? null);
  }
  return (
    <form className="alv-clinical__entry-form" onSubmit={submit} noValidate aria-label={submitLabel}>
      {children}
      <div className="alv-form-field">
        <label htmlFor={id} className="alv-form-field__label">{label}</label>
        <textarea id={id} className="alv-form-field__input alv-clinical__details" rows={2} maxLength={500} value={reason} aria-invalid={error ? true : undefined} onChange={(e) => setReason(e.target.value)} />
        {hint && !error && <p className="alv-form-field__hint">{hint}</p>}
        {error && <p className="alv-form-field__error" role="alert">{error}</p>}
      </div>
      <div className="alv-clinical__row-actions">
        <Button type="submit" variant="primary" disabled={busy}>{submitLabel}</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}

/**
 * ALV-005-C01: one item of the patient's clinical record - its name and details, its status in words, who added it and who last changed it - with the changes a clinician
 * may make: correct the details, change the status (an allergy resolved, a medication discontinued), or remove it as entered in error (a reason is required, and the item
 * stays in its history). Nothing is overwritten: every change adds a version that the history shows.
 */
export function RecordItemRow({ item, canWrite, busy, run }: Props) {
  const [mode, setMode] = useState<Mode>("view");
  const [status, setStatus] = useState<ItemStatus>(item.status);
  const statusId = useId();
  const text = details(item);

  return (
    <li className="alv-clinical__entry">
      {mode === "edit" ? (
        <EntryForm
          kind={item.kind}
          initial={entryInputOf({ ...item, createdByUserId: null })}
          submitLabel="Save changes"
          busy={busy}
          onCancel={() => setMode("view")}
          onSubmit={async (input) => {
            const refused = await run(() => updateRecordItem(item.id, input, item.rowVersion), "Item changes saved.");
            if (!refused) setMode("view");
            return refused;
          }}
        />
      ) : (
        <>
          <div className="alv-clinical__entry-main">
            <p className="alv-clinical__entry-name">
              {item.name} <span className={`alv-clinical__badge alv-clinical__badge--item-${item.status.toLowerCase()}`}>{item.status}</span>
            </p>
            {text && <p className="alv-clinical__entry-details">{text}</p>}
            <p className="alv-clinical__meta">
              Added by {item.createdByName ?? "a staff member"} on {when(item.createdAtUtc)}
              {item.updatedAtUtc ? ` · Last changed by ${item.updatedByName ?? "a staff member"} on ${when(item.updatedAtUtc)}` : ""}
            </p>
            <ItemHistory itemId={item.id} itemName={item.name} version={item.rowVersion} />
            {mode === "status" && (
              <ReasonForm
                label="Reason (optional)"
                required={false}
                submitLabel="Save status"
                busy={busy}
                onCancel={() => setMode("view")}
                onSubmit={async (reason) => {
                  const refused = await run(() => setItemStatus(item.id, status, item.rowVersion, reason), `${item.name} is now ${status.toLowerCase()}.`);
                  if (!refused) setMode("view");
                  return refused;
                }}
              >
                <div className="alv-form-field">
                  <label htmlFor={statusId} className="alv-form-field__label">Status of {item.name}</label>
                  <select id={statusId} className="alv-form-field__input" value={status} onChange={(e) => setStatus(e.target.value as ItemStatus)}>
                    {statusesFor(item.kind).map((s) => <option key={s} value={s}>{s}</option>)}
                  </select>
                </div>
              </ReasonForm>
            )}
            {mode === "remove" && (
              <ReasonForm
                label="Why was it entered in error?"
                hint="The item is kept, with this reason, in its history."
                required
                submitLabel="Remove as entered in error"
                busy={busy}
                onCancel={() => setMode("view")}
                onSubmit={async (reason) => {
                  const refused = await run(() => removeRecordItem(item.id, item.rowVersion, reason), `${item.name} removed as entered in error.`);
                  if (!refused) setMode("view");
                  return refused;
                }}
              />
            )}
          </div>
          {canWrite && mode === "view" && (
            <div className="alv-clinical__row-actions">
              <Button type="button" onClick={() => setMode("edit")} disabled={busy} aria-label={`Correct details of ${item.name}`}>Correct details</Button>
              <Button type="button" onClick={() => { setStatus(item.status); setMode("status"); }} disabled={busy} aria-label={`Change status of ${item.name}`}>Change status</Button>
              <Button type="button" onClick={() => setMode("remove")} disabled={busy} aria-label={`Entered in error: ${item.name}`}>Entered in error</Button>
            </div>
          )}
        </>
      )}
    </li>
  );
}

