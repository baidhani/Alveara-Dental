import { useId, useState } from "react";
import { Button } from "../../components/Button";
import { ENTRY_NOUNS, SECTION_LABELS, SECTION_STATUS_LABELS, entryInputOf } from "../../services/clinicalApi";
import type { EncounterEntry, EncounterSection, EntryInput, ExistingItem } from "../../services/clinicalApi";
import { statusesFor } from "../../services/clinicalRecordApi";
import type { RecordItem } from "../../services/clinicalRecordApi";
import { showInstant } from "../../time/practiceTime";
import { EntryForm } from "./EntryForm";
import type { SaveResult } from "./SectionPanel";
import "./Clinical.css";

interface Props {
  section: EncounterSection;
  editable: boolean;
  busy: boolean;
  /** The chart item a refused add pointed to (Addendum 02), for this section; null otherwise. */
  existing: ExistingItem | null;
  onDismissExisting: () => void;
  onAdd: (kind: string, input: EntryInput) => Promise<SaveResult>;
  onUpdate: (item: RecordItem, input: EntryInput) => Promise<SaveResult>;
  onSetStatus: (item: RecordItem, status: string) => Promise<SaveResult>;
  onRemove: (item: RecordItem, reason: string) => Promise<SaveResult>;
  onConfirm: (kind: string) => Promise<void>;
  onMarkNone: (kind: string) => Promise<void>;
  onClearReview: (kind: string) => Promise<void>;
}

const details = (e: { reaction: string | null; severity: string | null; dose: string | null; frequency: string | null; detail: string | null }) =>
  [e.reaction && `Reaction: ${e.reaction}`, e.severity && `Severity: ${e.severity}`, e.dose && `Dose: ${e.dose}`, e.frequency && `Frequency: ${e.frequency}`, e.detail]
    .filter(Boolean).join(" · ");

const CHANGE_WORDS: Record<string, string> = { Added: "added", Changed: "changed", StatusChanged: "status changed", RemovedInError: "removed as entered in error" };

/** Asks for the status the user wants; nothing is preselected and nothing changes until it is submitted (Addendum 02). */
function StatusForm({ item, busy, onSubmit, onCancel }: { item: RecordItem; busy: boolean; onSubmit: (status: string) => Promise<SaveResult>; onCancel: () => void }) {
  const id = useId();
  const [status, setStatus] = useState("");
  return (
    <form className="alv-clinical__section-actions" onSubmit={async (ev) => { ev.preventDefault(); if (status && !(await onSubmit(status))) onCancel(); }}>
      <label htmlFor={id} className="alv-form-field__label">New status for {item.name} (now {item.status.toLowerCase()})</label>
      <select id={id} value={status} onChange={(ev) => setStatus(ev.target.value)} disabled={busy}>
        <option value="">Choose a status</option>
        {statusesFor(item.kind).filter((s) => s !== item.status).map((s) => <option key={s} value={s}>{s}</option>)}
      </select>
      <Button type="submit" variant="primary" disabled={busy || !status}>Save status</Button>
      <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
    </form>
  );
}

/** Removing an item this encounter added: it is marked entered in error with a reason, never deleted. */
function RemoveForm({ item, busy, onSubmit, onCancel }: { item: RecordItem; busy: boolean; onSubmit: (reason: string) => Promise<SaveResult>; onCancel: () => void }) {
  const id = useId();
  const [reason, setReason] = useState("");
  return (
    <form className="alv-clinical__section-actions" onSubmit={async (ev) => { ev.preventDefault(); if (reason.trim() && !(await onSubmit(reason.trim()))) onCancel(); }}>
      <label htmlFor={id} className="alv-form-field__label">Why is {item.name} being removed? It stays in the history as entered in error.</label>
      <input id={id} value={reason} onChange={(ev) => setReason(ev.target.value)} disabled={busy} maxLength={500} />
      <Button type="submit" variant="primary" disabled={busy || !reason.trim()}>Remove as entered in error</Button>
      <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
    </form>
  );
}

/**
 * ALV-N022 A1: an encounter's allergy, medication or medical-history section. The patient's CHART is the one record: the section lists the chart as it is now, what this
 * encounter changed on it, the statement made here, and anything the encounter recorded before the chart became the one record (read-only). Adding something already on
 * the chart never creates a second item: the server points to it, and the screen offers to update it (active), reactivate or update it (not active), or - for medications
 * and history - confirm the list is current. Cancelling changes nothing.
 */
export function ChartSectionPanel(props: Props) {
  const { section, editable, busy, existing } = props;
  const [adding, setAdding] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [statusId, setStatusId] = useState<string | null>(null);
  const [removingId, setRemovingId] = useState<string | null>(null);
  const label = SECTION_LABELS[section.kind] ?? section.kind;
  const noun = ENTRY_NOUNS[section.kind] ?? "entry";
  const chart = section.chartItems ?? [];
  const here = section.recordedHere ?? [];
  const legacy: EncounterEntry[] = section.entries;
  const anyActive = chart.some((i) => i.status === "Active");
  const addedHere = (id: string) => here.some((p) => p.itemId === id && p.versionNumber === 1);
  const existingItem = existing ? chart.find((i) => i.id === existing.id) ?? null : null;
  const closeExisting = () => props.onDismissExisting();

  return (
    <section className="alv-clinical__section" aria-label={label}>
      <header className="alv-clinical__section-head">
        <h3 className="alv-clinical__section-title">{label}</h3>
        <span className={`alv-clinical__section-status alv-clinical__section-status--${section.status.toLowerCase()}`}>{SECTION_STATUS_LABELS[section.status]}</span>
      </header>

      {section.contradiction && <p className="alv-clinical__note alv-clinical__note--warning" role="note">{section.contradiction}</p>}

      <h4 className="alv-clinical__subhead">On the patient's chart now</h4>
      {chart.length === 0 ? <p className="alv-clinical__meta">Nothing is listed on the chart.</p> : (
        <ul className="alv-clinical__entries">
          {chart.map((item) => (
            <li key={item.id} className="alv-clinical__entry">
              {editingId === item.id ? (
                <EntryForm kind={section.kind} initial={entryInputOf(item)} submitLabel="Save changes" busy={busy} onCancel={() => setEditingId(null)}
                  onSubmit={async (input) => { const refused = await props.onUpdate(item, input); if (!refused) { setEditingId(null); closeExisting(); } return refused; }} />
              ) : statusId === item.id ? (
                <StatusForm item={item} busy={busy} onCancel={() => setStatusId(null)} onSubmit={async (s) => { const r = await props.onSetStatus(item, s); if (!r) closeExisting(); return r; }} />
              ) : removingId === item.id ? (
                <RemoveForm item={item} busy={busy} onCancel={() => setRemovingId(null)} onSubmit={(reason) => props.onRemove(item, reason)} />
              ) : (
                <>
                  <div className="alv-clinical__entry-main">
                    <p className="alv-clinical__entry-name">{item.name} <span className="alv-clinical__meta">({item.status.toLowerCase()})</span></p>
                    {details(item) && <p className="alv-clinical__entry-details">{details(item)}</p>}
                  </div>
                  {editable && (
                    <div className="alv-clinical__row-actions">
                      <Button type="button" onClick={() => setEditingId(item.id)} disabled={busy} aria-label={`Change ${item.name}`}>Change</Button>
                      <Button type="button" onClick={() => setStatusId(item.id)} disabled={busy} aria-label={`Change status of ${item.name}`}>Change status</Button>
                      {addedHere(item.id) && <Button type="button" onClick={() => setRemovingId(item.id)} disabled={busy} aria-label={`Remove ${item.name}`}>Remove</Button>}
                    </div>
                  )}
                </>
              )}
            </li>
          ))}
        </ul>
      )}

      {existing && (
        <div className="alv-clinical__note" role="alert">
          <p>
            {existingItem ? existingItem.name : "That item"} is already on the chart
            {existing.status === "Active" ? "." : ` as ${existing.status.toLowerCase()}.`} Nothing was added.
          </p>
          <div className="alv-clinical__row-actions">
            {existing.status === "Active" ? (
              <Button type="button" onClick={() => setEditingId(existing.id)} disabled={busy || !existingItem}>Update the existing item</Button>
            ) : (
              <Button type="button" onClick={() => setStatusId(existing.id)} disabled={busy || !existingItem}>Reactivate or update the existing item</Button>
            )}
            {section.kind !== "Allergy" && <Button type="button" onClick={async () => { await props.onConfirm(section.kind); closeExisting(); }} disabled={busy}>Confirm the list is current</Button>}
            <Button type="button" onClick={closeExisting} disabled={busy}>Cancel</Button>
          </div>
        </div>
      )}

      {here.length > 0 && (
        <>
          <h4 className="alv-clinical__subhead">Recorded on the chart in this encounter</h4>
          <ul className="alv-clinical__history-list">
            {here.map((p) => <li key={`${p.itemId}-${p.versionNumber}`}>{p.name}: {CHANGE_WORDS[p.changeType] ?? p.changeType} ({p.status.toLowerCase()}) by {p.actorName ?? "a staff member"}, {showInstant(p.occurredAtUtc)}</li>)}
          </ul>
        </>
      )}
      {section.reviewedHere === "Reviewed" && <p className="alv-clinical__note">Confirmed in this encounter: the chart's list is current.</p>}
      {section.reviewedHere === "NoneKnown" && <p className="alv-clinical__note">Reviewed in this encounter: nothing to report here.</p>}
      {section.reviewedHere === "Unknown" && <p className="alv-clinical__note">Marked unknown in this encounter.</p>}

      {legacy.length > 0 && (
        <>
          <h4 className="alv-clinical__subhead">Recorded in this encounter before the chart became the one record (kept as it was)</h4>
          <ul className="alv-clinical__entries">
            {legacy.map((e) => <li key={e.id} className="alv-clinical__entry"><p className="alv-clinical__entry-name">{e.name}</p>{details(e) && <p className="alv-clinical__entry-details">{details(e)}</p>}</li>)}
          </ul>
        </>
      )}
      {section.status === "NoneReported" && !section.reviewedHere && <p className="alv-clinical__note">Reviewed with the patient: nothing to report here (recorded before the chart became the one record).</p>}

      {editable && (
        <div className="alv-clinical__section-actions">
          {adding ? (
            <EntryForm kind={section.kind} submitLabel={`Add ${noun}`} busy={busy} onCancel={() => setAdding(false)}
              onSubmit={async (input) => { const refused = await props.onAdd(section.kind, input); if (!refused) setAdding(false); return refused; }} />
          ) : (
            <>
              <Button type="button" onClick={() => { closeExisting(); setAdding(true); }} disabled={busy} aria-label={`Add ${noun}: ${label}`}>Add {noun}</Button>
              {chart.length > 0 && section.reviewedHere !== "Reviewed" && (
                <Button type="button" onClick={() => void props.onConfirm(section.kind)} disabled={busy} aria-label={`Confirm the list is current: ${label}`}>Confirm the list is current</Button>
              )}
              {!anyActive && section.reviewedHere !== "NoneKnown" && (
                <Button type="button" onClick={() => void props.onMarkNone(section.kind)} disabled={busy} aria-label={`Reviewed - none reported: ${label}`}>Reviewed - none reported</Button>
              )}
              {section.reviewedHere && (
                <Button type="button" onClick={() => void props.onClearReview(section.kind)} disabled={busy} aria-label={`Withdraw this encounter's statement: ${label}`}>Withdraw this encounter's statement</Button>
              )}
            </>
          )}
        </div>
      )}
    </section>
  );
}
