import { useEffect, useId, useRef, useState } from "react";
import { Button } from "../../../components/Button";
import { useUnsavedChangesWarning } from "../../../hooks/useUnsavedChangesWarning";
import { NOTE_LABELS } from "../../../services/clinicalNotesApi";
import type { SaveResult } from "../SectionPanel";
import "../Clinical.css";

/** How long typing must pause before a note is saved on its own. Exported so a test can wait for it. */
export const AUTOSAVE_MS = 1200;

interface Props {
  section: string;
  /** What the server holds for this note (empty when it has not been saved yet). */
  saved: string;
  required: boolean;
  /** The template's starter text, when the note came from one and has not been written over - shown as a hint that it still needs writing. */
  starterUnchanged: boolean;
  editable: boolean;
  updatedBy: string | null;
  onSave: (section: string, body: string) => Promise<SaveResult>;
}

/**
 * ALV-005-C01: one note (a SOAP section, a progress note or a treatment note). While the encounter is an editable draft it saves ITSELF: shortly after typing pauses, and
 * when the box loses focus. The typed text lives here until the server has accepted it - a failed or dropped save keeps it on screen, says so in words, and offers
 * "Save note" to try again. A failed autosave is not retried in a loop: it is tried again only when the clinician types more or asks. When the server's copy changes
 * under an untouched box (another save, a reload) the box follows it; when the box has unsaved typing it never replaces it.
 */
export function NoteEditor({ section, saved, required, starterUnchanged, editable, updatedBy, onSave }: Props) {
  const [text, setText] = useState(saved);
  const [failed, setFailed] = useState(false);
  const lastServer = useRef(saved);
  const lastSent = useRef(saved);
  const savedNow = useRef(saved);
  savedNow.current = saved;
  const id = useId();
  const label = NOTE_LABELS[section] ?? section;
  const dirty = text !== saved;
  useUnsavedChangesWarning(editable && dirty);

  // follow the server's copy only while there is nothing typed that it does not have
  useEffect(() => {
    if (text === lastServer.current) setText(saved);
    lastServer.current = saved;
    lastSent.current = saved;
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `text` is read only to decide whether the box is untouched; changing it must not re-run this
  }, [saved]);

  async function flush(value: string) {
    lastSent.current = value;
    const refused = await onSave(section, value);
    setFailed(refused !== null);
  }

  // autosave: a pause in typing saves; the same text is never sent twice in a row, so a failure cannot loop
  useEffect(() => {
    if (!editable || text === saved || text === lastSent.current) return;
    // the timer re-checks when it fires: a save made meanwhile (on blur, or the button) must not be sent a second time
    const timer = window.setTimeout(() => { if (text !== lastSent.current && text !== savedNow.current) void flush(text); }, AUTOSAVE_MS);
    return () => window.clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- only a change of the typed text schedules a save
  }, [text]);

  if (!editable) {
    return (
      <div className="alv-clinical__note-block">
        <h4 className="alv-clinical__note-title">{label}</h4>
        {saved.trim() === "" ? <p className="alv-clinical__meta">Nothing was written.</p> : <p className="alv-clinical__note-body">{saved}</p>}
        {updatedBy && <p className="alv-clinical__meta">Last saved by {updatedBy}</p>}
      </div>
    );
  }

  return (
    <div className="alv-clinical__note-block">
      <label htmlFor={id} className="alv-form-field__label">{label}{required ? " (required by the template)" : ""}</label>
      <textarea
        id={id}
        className="alv-form-field__input alv-clinical__details alv-clinical__note-input"
        rows={5}
        maxLength={8000}
        value={text}
        aria-describedby={`${id}-state`}
        onChange={(e) => setText(e.target.value)}
        onBlur={() => {
          if (text !== saved && text !== lastSent.current) void flush(text);
        }}
      />
      <p id={`${id}-state`} className="alv-clinical__meta">
        {failed ? "Not saved - your text is still here." : dirty ? "Unsaved changes - saving when you pause." : starterUnchanged ? "Starter text only - write the note." : updatedBy ? `Saved. Last saved by ${updatedBy}.` : "Nothing saved yet."}
      </p>
      {(failed || dirty) && (
        <Button type="button" onClick={() => void flush(text)} aria-label={`Save ${label.toLowerCase()} now`}>Save note</Button>
      )}
    </div>
  );
}
