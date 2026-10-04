import { useState } from "react";
import { Button } from "../../../components/Button";
import { ENTRY_NOUNS, SECTION_LABELS } from "../../../services/clinicalApi";
import { SECTION_STATUS_WORDS, addRecordItem, setSectionReview } from "../../../services/clinicalRecordApi";
import type { ClinicalRecord, RecordSection } from "../../../services/clinicalRecordApi";
import { EntryForm } from "../EntryForm";
import type { SaveResult } from "../SectionPanel";
import { RecordItemRow } from "./RecordItemRow";
import "../Clinical.css";

const when = (iso: string) => new Date(iso).toLocaleDateString();

/** What a clinician is told about a section, in words (never by colour alone): who said what, and when. */
function sectionStatement(section: RecordSection): string {
  const by = section.reviewedByName ?? "a staff member";
  const on = section.reviewedAtUtc ? when(section.reviewedAtUtc) : "";
  switch (section.status) {
    case "Reviewed": return `Reviewed by ${by} on ${on}`;
    case "NeedsReview": return section.reviewedAtUtc ? `Changed since ${by} reviewed it on ${on}` : "Listed, but nobody has confirmed the list is current";
    case "NoneKnown": return `Reviewed by ${by} on ${on}: nothing known to report`;
    case "Unknown": return `Marked unknown by ${by} on ${on}: it could not be established`;
    default: return "Nobody has reviewed this section yet";
  }
}

interface Props {
  patientId: string;
  section: RecordSection;
  canWrite: boolean;
  busy: boolean;
  run: (change: () => Promise<ClinicalRecord>, savedText: string) => Promise<SaveResult>;
}

/**
 * ALV-005-C01: one section of the patient's clinical record. It lists the items with their status, and says in words where the section stands: reviewed (with who and
 * when), needs review (an item changed after the last review), none known, unknown, or not reviewed. "None known" and "unknown" are statements a clinician makes - they are
 * never entered as an item - so a section is never completed by inventing a fact.
 */
export function RecordSectionCard({ patientId, section, canWrite, busy, run }: Props) {
  const [adding, setAdding] = useState(false);
  const label = SECTION_LABELS[section.kind] ?? section.kind;
  const noun = ENTRY_NOUNS[section.kind] ?? "item";
  const stated = section.status === "NoneKnown" || section.status === "Unknown";

  return (
    <section className="alv-clinical__section" aria-label={`Record: ${label}`}>
      <header className="alv-clinical__section-head">
        <h4 className="alv-clinical__section-title">{label}</h4>
        <span className={`alv-clinical__section-status alv-clinical__section-status--rec-${section.status.toLowerCase()}`}>{SECTION_STATUS_WORDS[section.status]}</span>
      </header>
      <p className="alv-clinical__meta">{sectionStatement(section)}</p>

      {section.items.length > 0 && (
        <ul className="alv-clinical__entries">
          {section.items.map((item) => (
            <RecordItemRow key={item.id} item={item} canWrite={canWrite} busy={busy} run={run} />
          ))}
        </ul>
      )}

      {canWrite && (
        <div className="alv-clinical__section-actions">
          {adding ? (
            <EntryForm
              kind={section.kind}
              submitLabel={`Add ${noun}`}
              busy={busy}
              onCancel={() => setAdding(false)}
              onSubmit={async (input) => {
                const refused = await run(() => addRecordItem(patientId, section.kind, input), `${label} item saved.`);
                if (!refused) setAdding(false);
                return refused;
              }}
            />
          ) : (
            <>
              <Button type="button" onClick={() => setAdding(true)} disabled={busy} aria-label={`Add ${noun} to the record: ${label}`}>Add {noun}</Button>
              {section.items.length > 0 && (
                <Button type="button" onClick={() => void run(() => setSectionReview(patientId, section.kind, "Reviewed"), `${label} confirmed current.`)} disabled={busy || section.status === "Reviewed"} aria-label={`Confirm the ${label.toLowerCase()} list is current`}>
                  Confirm list is current
                </Button>
              )}
              {section.items.length === 0 && !stated && (
                <>
                  <Button type="button" onClick={() => void run(() => setSectionReview(patientId, section.kind, "NoneKnown"), `${label} marked none known.`)} disabled={busy} aria-label={`None known: ${label}`}>None known</Button>
                  <Button type="button" onClick={() => void run(() => setSectionReview(patientId, section.kind, "Unknown"), `${label} marked unknown.`)} disabled={busy} aria-label={`Unknown: ${label}`}>Unknown</Button>
                </>
              )}
              {stated && (
                <Button type="button" onClick={() => void run(() => setSectionReview(patientId, section.kind, "NotReviewed"), `${label} statement withdrawn.`)} disabled={busy} aria-label={`Withdraw the statement: ${label}`}>
                  Withdraw this statement
                </Button>
              )}
            </>
          )}
        </div>
      )}
    </section>
  );
}
