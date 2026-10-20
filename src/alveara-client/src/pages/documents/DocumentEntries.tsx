import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { THUMBNAIL_MAX_BYTES, THUMBNAIL_TYPES, contentUrl, thumbnailUrl } from "../../services/documentsApi";
import type { DocumentView, LibraryEntry } from "../../services/documentsApi";
import type { EncounterSummary } from "../../services/clinicalApi";
import { REGION_LABELS } from "../diagnosis/diagnosisStructureRules";
import { DEFAULT_NUMBERING, displayTooth } from "../odontogram/toothNumbering";
import { INLINE_TYPES, encounterLabel, formatBytes, formatDate, formatDateTime } from "./documentText";

export interface EntryHandlers {
  canEdit: boolean;
  onEdit: (id: string) => void;
  onInactivate: (id: string) => void;
  onReactivate: (id: string) => void;
  onPreview: (id: string) => void;
  onDetails: (id: string) => void;
}

const damaged = (d: DocumentView | null) => d?.integrityStatus === "Missing" || d?.integrityStatus === "Corrupt";

/** The words that say what is special about an entry: a duplicate, a voided signed form, an inactive document, a file that is missing or damaged. Status is never carried by colour alone. */
export function EntryBadges({ entry }: { entry: LibraryEntry }) {
  const d = entry.document;
  return (
    <>
      {entry.isDuplicate && <span className="alv-documents__badge">Duplicate</span>}
      {entry.isVoided && <span className="alv-documents__badge alv-documents__badge--void">Voided</span>}
      {entry.isInactive && <span className="alv-documents__badge">Inactive</span>}
      {d?.integrityStatus === "Missing" && <span className="alv-documents__badge alv-documents__badge--void" title={d.integrityCheckedAtUtc ? `Checked ${formatDateTime(d.integrityCheckedAtUtc)}` : undefined}>File missing</span>}
      {d?.integrityStatus === "Corrupt" && <span className="alv-documents__badge alv-documents__badge--void" title={d.integrityCheckedAtUtc ? `Checked ${formatDateTime(d.integrityCheckedAtUtc)}` : undefined}>File damaged</span>}
    </>
  );
}

/** What a document is about: its encounter, one tooth or one region. */
export function EntryAbout({ document, encounters }: { document: DocumentView | null; encounters: EncounterSummary[] }) {
  if (!document || (!document.encounterId && !document.toothKey && !document.regionKey)) return <>—</>;
  const encounter = encounters.find((e) => e.id === document.encounterId);
  return (
    <span className="alv-documents__chips">
      {document.encounterId && <span className="alv-documents__chip">{encounter ? `Encounter ${encounterLabel(encounter.encounterAtUtc)}` : "Encounter"}</span>}
      {document.toothKey && <span className="alv-documents__chip">{`Tooth ${displayTooth(document.toothKey, DEFAULT_NUMBERING)}`}</span>}
      {document.regionKey && <span className="alv-documents__chip">{REGION_LABELS[document.regionKey] ?? document.regionKey}</span>}
    </span>
  );
}

export function EntryActions({ entry, patientId, handlers }: { entry: LibraryEntry; patientId: string; handlers: EntryHandlers }) {
  if (entry.kind === "SignedForm") return <Link to={`/patients/${patientId}/forms/${entry.patientFormId}`} aria-label={`View form ${entry.title}`}>View form</Link>;
  const isPicture = entry.detectedType === "png" || entry.detectedType === "jpeg";
  return (
    <>
      {INLINE_TYPES.has(entry.detectedType) && <a href={contentUrl(entry.id)} target="_blank" rel="noopener noreferrer" aria-label={`Open ${entry.title}`}>Open</a>}
      <a href={contentUrl(entry.id, true)} aria-label={`Download ${entry.title}`}>Download</a>
      {isPicture && <Button type="button" onClick={() => handlers.onPreview(entry.id)} aria-label={`Preview ${entry.title}`}>Preview</Button>}
      <Button type="button" onClick={() => handlers.onDetails(entry.id)} aria-label={`Details ${entry.title}`}>Details</Button>
      {handlers.canEdit && <Button type="button" onClick={() => handlers.onEdit(entry.id)} aria-label={`Edit ${entry.title}`}>Edit</Button>}
      {handlers.canEdit && (entry.isInactive
        ? <Button type="button" onClick={() => handlers.onReactivate(entry.id)} aria-label={`Reactivate ${entry.title}`}>Reactivate</Button>
        : <Button type="button" onClick={() => handlers.onInactivate(entry.id)} aria-label={`Inactivate ${entry.title}`}>Inactivate</Button>)}
    </>
  );
}

/** The picture of a PNG or JPEG the server will serve a thumbnail of (up to 5 MB, file present); a word for everything else. */
function Thumbnail({ entry }: { entry: LibraryEntry }) {
  const d = entry.document;
  if (entry.kind === "SignedForm") return <div className="alv-documents__thumb alv-documents__thumb--word">Signed form</div>;
  if (damaged(d)) return <div className="alv-documents__thumb alv-documents__thumb--word">{d?.integrityStatus === "Missing" ? "File missing" : "File damaged"}</div>;
  if (THUMBNAIL_TYPES.has(entry.detectedType)) {
    if ((entry.sizeBytes ?? 0) > THUMBNAIL_MAX_BYTES) return <div className="alv-documents__thumb alv-documents__thumb--word">Too large for a thumbnail</div>;
    return <div className="alv-documents__thumb"><img src={thumbnailUrl(entry.id)} alt={`Thumbnail of ${entry.title}`} loading="lazy" /></div>;
  }
  return <div className="alv-documents__thumb alv-documents__thumb--word">{entry.detectedType === "other" ? "File" : entry.detectedType.toUpperCase()}</div>;
}

export function DocumentTable({ entries, patientId, encounters, handlers }: { entries: LibraryEntry[]; patientId: string; encounters: EncounterSummary[]; handlers: EntryHandlers }) {
  return (
    <table className="alv-documents__table" aria-label="Documents">
      <thead>
        <tr><th scope="col">Name</th><th scope="col">Category</th><th scope="col">About</th><th scope="col">Date</th><th scope="col">Size</th><th scope="col">Imported by</th><th scope="col">Actions</th></tr>
      </thead>
      <tbody>
        {entries.map((entry) => (
          <tr key={`${entry.kind}-${entry.id}`}>
            <td>{entry.title}<EntryBadges entry={entry} /></td>
            <td>{entry.categoryName}</td>
            <td><EntryAbout document={entry.document} encounters={encounters} /></td>
            <td>{formatDate(entry.date)}</td>
            <td>{entry.sizeBytes === null ? "—" : formatBytes(entry.sizeBytes)}</td>
            <td title={formatDateTime(entry.atUtc)}>{entry.importedByName ?? "—"}</td>
            <td className="alv-documents__row-actions"><EntryActions entry={entry} patientId={patientId} handlers={handlers} /></td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export function DocumentGrid({ entries, patientId, encounters, handlers }: { entries: LibraryEntry[]; patientId: string; encounters: EncounterSummary[]; handlers: EntryHandlers }) {
  return (
    <ul className="alv-documents__grid" aria-label="Document thumbnails">
      {entries.map((entry) => (
        <li key={`${entry.kind}-${entry.id}`} className="alv-documents__card">
          <Thumbnail entry={entry} />
          <p className="alv-documents__card-title">{entry.title}<EntryBadges entry={entry} /></p>
          <p className="alv-documents__card-meta">{entry.categoryName}, {formatDate(entry.date)}</p>
          <p className="alv-documents__card-meta"><EntryAbout document={entry.document} encounters={encounters} /></p>
          <div className="alv-documents__row-actions"><EntryActions entry={entry} patientId={patientId} handlers={handlers} /></div>
        </li>
      ))}
    </ul>
  );
}
