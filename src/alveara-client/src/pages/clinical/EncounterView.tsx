import { useCallback, useEffect, useRef, useState } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { SafeLink } from "../../components/SafeLink";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import {
  EVENT_LABELS, SECTION_LABELS, addEntry, clearSectionReview, clinicalFieldErrorsOf, finalizeEncounter, getEncounter, isNetworkFailure, markNoneReported, removeEntry, updateEntry,
} from "../../services/clinicalApi";
import type { EncounterDetail, EntryInput } from "../../services/clinicalApi";
import { NOTE_LABELS, applyTemplate, saveNote, signEncounter, unsignEncounter } from "../../services/clinicalNotesApi";
import type { PatientDetail } from "../../services/patientsApi";
import { AddendumPanel } from "./AddendumPanel";
import { FinalizeReview } from "./FinalizeReview";
import { NotesPanel } from "./notes/NotesPanel";
import { VitalsPanel } from "./notes/VitalsPanel";
import { SafetyStrip } from "../safety/SafetyStrip";
import { SectionPanel } from "./SectionPanel";
import type { SaveResult } from "./SectionPanel";
import "./Clinical.css";

type Load = { kind: "loading" } | { kind: "not-found" } | { kind: "error" } | { kind: "loaded"; encounter: EncounterDetail };
type Saving = { kind: "idle" } | { kind: "saving" } | { kind: "saved"; text: string } | { kind: "failed"; text: string };

const when = (iso: string) => new Date(iso).toLocaleString();

/**
 * STORY-005: one encounter - document medical and dental history, allergies and medications while it is a draft, review and finalize it, and read it (with its addenda
 * and history) afterwards. An encounter that does not belong to the patient in context is never shown.
 *
 * Every change is sent to the server as it is made and the encounter is replaced by what the server returns, so what is on screen is always what is stored; the status
 * line says in words whether the last change was saved, is saving, or failed. Typed values live in the entry forms and are kept when a save fails or conflicts.
 * A stale edit (someone else changed the note) shows the shared conflict banner and a reload that refreshes IN PLACE - the forms stay mounted, so nothing typed is lost.
 *
 * ALV-005-C01 adds the encounter's notes (SOAP, progress, treatment; they autosave), vital signs, note template, and signing: a signed note is locked until it is finalized
 * or unsigned. Changes are sent ONE AT A TIME, each from the version the previous one returned, so an autosave and another change can never race each other into a conflict
 * with the clinician's own work.
 * Permissions only decide which controls are drawn; they never decide when data is loaded (a permission arriving late must not reload and wipe the screen).
 */
export function EncounterView({ patient, encounterId }: { patient: PatientDetail; encounterId: string }) {
  const { hasPermission } = useAuth();
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [saving, setSaving] = useState<Saving>({ kind: "idle" });
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const [reviewing, setReviewing] = useState(false);
  const [refusal, setRefusal] = useState<{ message: string; sections: string[] } | null>(null);
  const latest = useRef<EncounterDetail | null>(null);
  const queue = useRef<Promise<unknown>>(Promise.resolve());

  const adopt = useCallback((encounter: EncounterDetail) => {
    latest.current = encounter;
    setLoad({ kind: "loaded", encounter });
    setConflict(null);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    getEncounter(encounterId, controller.signal)
      .then((encounter) => (encounter.patientId === patient.id ? adopt(encounter) : setLoad({ kind: "not-found" })))
      .catch((err) => {
        if (controller.signal.aborted) return;
        setLoad(err instanceof ApiError && err.status === 404 ? { kind: "not-found" } : { kind: "error" });
      });
    return () => controller.abort();
  }, [encounterId, patient.id, adopt]);

  const encounter = load.kind === "loaded" ? load.encounter : null;
  const canWrite = hasPermission("ManageClinicalNotes");
  const editable = encounter?.status === "Draft" && !encounter.isSigned && canWrite;
  const busy = saving.kind === "saving";

  /** Runs one change against the version on screen, after any change already in flight; null means it was saved, an object means it was not (with the server's per-field messages, if it gave any). */
  function run(change: (rowVersion: string) => Promise<EncounterDetail>, savedText: string): Promise<SaveResult> {
    const result = queue.current.then(() => runNow(change, savedText));
    queue.current = result.catch(() => undefined);
    return result;
  }

  async function runNow(change: (rowVersion: string) => Promise<EncounterDetail>, savedText: string): Promise<SaveResult> {
    const current = latest.current;
    if (!current) return {};
    setSaving({ kind: "saving" });
    setRefusal(null);
    try {
      adopt(await change(current.rowVersion));
      setSaving({ kind: "saved", text: savedText });
      return null;
    } catch (err) {
      if (isConcurrencyConflict(err)) {
        setConflict(err.body);
        setSaving({ kind: "failed", text: "Not saved: someone else changed this note." });
      } else if (err instanceof ApiError && err.code === "documentation_incomplete") {
        setRefusal({ message: err.message, sections: Object.keys(clinicalFieldErrorsOf(err)) });
        setSaving({ kind: "failed", text: "Not finalized: some sections still need attention." });
      } else if (isNetworkFailure(err)) {
        setSaving({ kind: "failed", text: "Not saved: the connection dropped. What you typed is still here; try again." });
      } else {
        setSaving({ kind: "failed", text: `Not saved: ${err instanceof ApiError ? err.message : "something went wrong."}` });
      }
      return clinicalFieldErrorsOf(err);
    }
  }

  /** Re-reads the encounter without leaving the screen: open forms and typed text stay exactly as they are. */
  async function reload() {
    try {
      adopt(await getEncounter(encounterId));
      setSaving({ kind: "idle" });
    } catch {
      setSaving({ kind: "failed", text: "Could not reload. Check your connection and try again." });
    }
  }

  if (load.kind === "loading") return <LoadingState label="Loading the encounter…" />;
  if (load.kind === "not-found") {
    return (
      <ErrorState
        title="That encounter was not found"
        description="It may belong to a different patient, or the link may be wrong."
        action={<SafeLink to={`/patients/${patient.id}/clinical`} className="alv-button alv-button--primary">Back to the encounters</SafeLink>}
      />
    );
  }
  if (load.kind === "error") return <ErrorState title="Could not load the encounter" description="Check your connection and reload the page." />;

  const e = load.encounter;
  const draft = e.status === "Draft";
  const canAct = draft && canWrite; // may review, sign, unsign and finalize (a signed draft is locked for editing, not for finalizing)

  return (
    <article className="alv-clinical" aria-labelledby="alv-clinical-title">
      <SafeLink to={`/patients/${patient.id}/clinical`} className="alv-workspace__link alv-clinical__back">All encounters</SafeLink>
      <header className="alv-clinical__head">
        <div>
          <h2 id="alv-clinical-title" className="alv-workspace__section-title">Encounter on {when(e.encounterAtUtc)}</h2>
          <p className="alv-clinical__meta">{e.appointmentId ? "Linked to an appointment. " : ""}Started {when(e.createdAtUtc)}</p>
        </div>
        <span className={`alv-clinical__badge alv-clinical__badge--${draft && e.isSigned ? "signed" : e.status.toLowerCase()}`}>
          {!draft ? "Finalized" : e.isSigned ? "Signed - awaiting finalize" : "Draft - not finalized"}
        </span>
      </header>

      <SafetyStrip patientId={patient.id} label="Patient safety for this encounter" />

      <p className="alv-clinical__status" role="status" aria-live="polite">
        {saving.kind === "saving" ? "Saving…" : saving.kind === "saved" ? saving.text : saving.kind === "failed" ? saving.text : draft ? (e.isSigned ? "This note is signed and locked." : "Every change is saved as you make it.") : "This note is finalized."}
      </p>

      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => void reload()} />}

      {!draft && (
        <p className="alv-clinical__note">
          Finalized {e.finalizedAtUtc ? when(e.finalizedAtUtc) : ""}. This note can no longer be changed. To correct or extend it, add an addendum below; the original stays exactly as it is.
        </p>
      )}
      {draft && !canWrite && <p className="alv-clinical__note">You can read this draft but your role cannot change it.</p>}
      {draft && e.isSigned && (
        <div className="alv-clinical__note">
          <p>Signed by {e.signedByName ?? "a staff member"}{e.signedAtUtc ? ` on ${when(e.signedAtUtc)}` : ""}. The note is locked: finalize it, or unsign it to keep editing.</p>
          {canWrite && <Button type="button" onClick={() => void run((v) => unsignEncounter(e.id, v), "Signature withdrawn.")} disabled={busy}>Unsign to keep editing</Button>}
        </div>
      )}

      {e.sections.map((section) => (
        <SectionPanel
          key={section.kind}
          section={section}
          editable={!!editable}
          busy={busy}
          onAdd={(kind, input: EntryInput) => run((v) => addEntry(e.id, kind, input, v), `${SECTION_LABELS[kind]} entry saved.`)}
          onUpdate={(entryId, input) => run((v) => updateEntry(e.id, entryId, input, v), "Entry changes saved.")}
          onRemove={async (entryId) => void (await run((v) => removeEntry(e.id, entryId, v), "Entry removed."))}
          onMarkNone={async (kind) => void (await run((v) => markNoneReported(e.id, kind, v), `${SECTION_LABELS[kind]} marked reviewed, none reported.`))}
          onClearReview={async (kind) => void (await run((v) => clearSectionReview(e.id, kind, v), `${SECTION_LABELS[kind]} review cleared.`))}
        />
      ))}

      <VitalsPanel encounter={e} editable={!!editable} busy={busy} run={run} />

      <NotesPanel
        encounter={e}
        editable={!!editable}
        busy={busy}
        onSave={(section, body) => run((v) => saveNote(e.id, section, body, v), `${NOTE_LABELS[section] ?? section} saved.`)}
        onApplyTemplate={(templateId) => run((v) => applyTemplate(e.id, templateId, v), "Template applied.")}
      />

      {canAct && !reviewing && (
        <div className="alv-clinical__finalize">
          <Button type="button" variant="primary" onClick={() => { setRefusal(null); setReviewing(true); }} disabled={busy}>Review and finalize</Button>
          {!e.isComplete && <p className="alv-clinical__note">Some sections are not yet addressed; the review shows which.</p>}
          {e.isComplete && e.missingNotes.length > 0 && <p className="alv-clinical__note">Some notes the template requires are not yet written; the review shows which.</p>}
        </div>
      )}
      {canAct && reviewing && (
        <FinalizeReview
          encounter={e}
          busy={busy}
          refusal={refusal}
          onCancel={() => { setReviewing(false); setRefusal(null); }}
          onSign={async () => void (await run((v) => signEncounter(e.id, v), "Encounter signed."))}
          onConfirm={async () => {
            if ((await run((v) => finalizeEncounter(e.id, v), "Encounter finalized.")) === null) setReviewing(false);
          }}
        />
      )}

      {!draft && <AddendumPanel encounter={e} canWrite={canWrite} onAdded={(updated) => { adopt(updated); setSaving({ kind: "saved", text: "Addendum added." }); }} />}

      <details className="alv-clinical__history">
        <summary>History of this encounter ({e.history.length})</summary>
        <ol className="alv-clinical__history-list">
          {e.history.map((h, i) => (
            <li key={`${h.occurredAtUtc}-${i}`}>
              <span className="alv-clinical__history-what">{EVENT_LABELS[h.eventType] ?? h.eventType}</span>
              {h.detail && <span> - {h.detail}</span>}
              <span className="alv-clinical__meta"> {when(h.occurredAtUtc)}</span>
            </li>
          ))}
        </ol>
      </details>
    </article>
  );
}
