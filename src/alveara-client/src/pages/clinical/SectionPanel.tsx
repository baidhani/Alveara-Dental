import { useState } from "react";
import { Button } from "../../components/Button";
import { ENTRY_NOUNS, SECTION_LABELS, SECTION_STATUS_LABELS, entryInputOf } from "../../services/clinicalApi";
import type { EncounterEntry, EncounterSection, EntryInput } from "../../services/clinicalApi";
import { EntryForm } from "./EntryForm";
import "./Clinical.css";

export type SaveResult = Record<string, string> | null;

interface Props {
  section: EncounterSection;
  /** True for a draft encounter and a clinician who may change it; a finalized encounter or a read-only role shows the section without controls. */
  editable: boolean;
  busy: boolean;
  onAdd: (kind: string, input: EntryInput) => Promise<SaveResult>;
  onUpdate: (entryId: string, input: EntryInput) => Promise<SaveResult>;
  onRemove: (entryId: string) => Promise<void>;
  onMarkNone: (kind: string) => Promise<void>;
  onClearReview: (kind: string) => Promise<void>;
}

/** The details of one entry, written the way a clinician reads them: reaction and severity for an allergy, dose and frequency for a medication. */
function EntryDetails({ entry }: { entry: EncounterEntry }) {
  const parts = [
    entry.reaction && `Reaction: ${entry.reaction}`,
    entry.severity && `Severity: ${entry.severity}`,
    entry.dose && `Dose: ${entry.dose}`,
    entry.frequency && `Frequency: ${entry.frequency}`,
    entry.detail,
  ].filter(Boolean);
  return parts.length > 0 ? <p className="alv-clinical__entry-details">{parts.join(" · ")}</p> : null;
}

/**
 * STORY-005: one of the four sections of an encounter (medical history, dental history, allergies, medications). It lists the active entries, lets a clinician add,
 * change and remove them while the encounter is a draft, and offers "Reviewed - none reported" so a section with nothing to report is completed without inventing
 * an entry. The section says in words whether it is recorded, reviewed with nothing to report, or not yet addressed.
 */
export function SectionPanel({ section, editable, busy, onAdd, onUpdate, onRemove, onMarkNone, onClearReview }: Props) {
  const [adding, setAdding] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const label = SECTION_LABELS[section.kind] ?? section.kind;
  const noun = ENTRY_NOUNS[section.kind] ?? "entry";
  const marked = section.status === "NoneReported";

  return (
    <section className="alv-clinical__section" aria-label={label}>
      <header className="alv-clinical__section-head">
        <h3 className="alv-clinical__section-title">{label}</h3>
        <span className={`alv-clinical__section-status alv-clinical__section-status--${section.status.toLowerCase()}`}>{SECTION_STATUS_LABELS[section.status]}</span>
      </header>

      {section.entries.length > 0 && (
        <ul className="alv-clinical__entries">
          {section.entries.map((entry) => (
            <li key={entry.id} className="alv-clinical__entry">
              {editingId === entry.id ? (
                <EntryForm
                  kind={section.kind}
                  initial={entryInputOf(entry)}
                  submitLabel="Save changes"
                  busy={busy}
                  onCancel={() => setEditingId(null)}
                  onSubmit={async (input) => {
                    const refused = await onUpdate(entry.id, input);
                    if (!refused) setEditingId(null);
                    return refused;
                  }}
                />
              ) : (
                <>
                  <div className="alv-clinical__entry-main">
                    <p className="alv-clinical__entry-name">{entry.name}</p>
                    <EntryDetails entry={entry} />
                  </div>
                  {editable && (
                    <div className="alv-clinical__row-actions">
                      <Button type="button" onClick={() => setEditingId(entry.id)} disabled={busy} aria-label={`Change ${entry.name}`}>Change</Button>
                      <Button type="button" onClick={() => void onRemove(entry.id)} disabled={busy} aria-label={`Remove ${entry.name}`}>Remove</Button>
                    </div>
                  )}
                </>
              )}
            </li>
          ))}
        </ul>
      )}

      {marked && <p className="alv-clinical__note">Reviewed with the patient: nothing to report here.</p>}
      {section.status === "Empty" && !editable && <p className="alv-clinical__note">Nothing was recorded for this section.</p>}

      {editable && (
        <div className="alv-clinical__section-actions">
          {adding ? (
            <EntryForm
              kind={section.kind}
              submitLabel={`Add ${noun}`}
              busy={busy}
              onCancel={() => setAdding(false)}
              onSubmit={async (input) => {
                const refused = await onAdd(section.kind, input);
                if (!refused) setAdding(false);
                return refused;
              }}
            />
          ) : marked ? (
            <Button type="button" onClick={() => void onClearReview(section.kind)} disabled={busy} aria-label={`Clear review to add entries: ${label}`}>
              Clear review to add entries
            </Button>
          ) : (
            <>
              <Button type="button" onClick={() => setAdding(true)} disabled={busy} aria-label={`Add ${noun}: ${label}`}>Add {noun}</Button>
              {section.entries.length === 0 && (
                <Button type="button" onClick={() => void onMarkNone(section.kind)} disabled={busy} aria-label={`Reviewed - none reported: ${label}`}>
                  Reviewed - none reported
                </Button>
              )}
            </>
          )}
        </div>
      )}
    </section>
  );
}
