import { useEffect, useId, useRef, useState } from "react";
import { useUnsavedChangesWarning } from "../../../hooks/useUnsavedChangesWarning";
import { ApiError } from "../../../services/authApi";
import { isNetworkFailure, newKey } from "../../../services/clinicalApi";
import { assessmentOf, checkPrescription, prescriptionProblemsOf } from "../../../services/prescriptionApi";
import type { AllergyAssessment, PrescriptionProblem } from "../../../services/prescriptionApi";
import {
  acknowledgeSafety, checkDraft, createDraft, discardDraft, finalizeDraft, getDraft, safetyChangeOf, updateDraft,
} from "../../../services/prescriptionWorkflowApi";
import type { DraftView, PrescriptionRecord, SafetyCheck, WorkflowOptions } from "../../../services/prescriptionWorkflowApi";
import type { SafetyContext } from "../../../services/safetyApi";
import { PrescriptionSafetyPanel } from "../PrescriptionSafetyPanel";
import { needsReview, reviewLabel } from "../prescriptionText";
import { FinalizeConfirmation } from "./FinalizeConfirmation";
import { EMPTY_FIELDS, entryOf, fieldsOf } from "./workflowRules";
import type { ComposerFields } from "./workflowRules";

const FIELD_IDS = ["medication", "doseValue", "doseUnit", "frequency", "instructions", "quantityValue", "quantityUnit", "refills", "reviewNote"] as const;

interface Change { fingerprint: string; added: string[]; removed: string[] }

interface Props {
  patientId: string;
  options: WorkflowOptions;
  /** The draft being worked on, or null for a new one. */
  draft: DraftView | null;
  /** The patient's safety context for the persistent panel, `null` while it loads, `"failed"` when it could not be read. */
  safety: SafetyContext | null | "failed";
  /** A draft was created or changed (the list refreshes and the composer keeps working on the saved draft). */
  onChanged: (draft: DraftView) => void;
  onClosed: () => void;
  onFinalized: (record: PrescriptionRecord, text: string) => void;
  onDiscarded: (text: string) => void;
}

/**
 * ALV-016-C01: the prescription composer. A prescription is written as a draft: it can be saved incomplete, edited, and finalized when it is complete. Beside it the persistent patient-safety panel shows what the
 * application knows (allergies, current medications, what was never reviewed), what the allergy check said, and always what is NOT checked (interactions, and whether a dose is clinically right).
 *
 * - Every wrong entry is named beside its box and what was typed is kept; a box that is not a number is put right on the screen and nothing is sent for it.
 * - If the allergy or medication list changed since the prescriber last acknowledged it, the changes are listed and finalizing waits for "I have read the changes": a change is never overridden silently.
 * - A match with a recorded allergy, or an allergy list nobody reviewed, asks for the prescriber's own words before finalizing.
 * - Finalizing goes through a confirmation that shows exactly what will be recorded, and says that it cannot be edited afterwards.
 * - A draft keeps the same save key while the entry is unchanged, so a retry or a double click cannot make two drafts.
 */
export function DraftComposer({ patientId, options, draft, safety, onChanged, onClosed, onFinalized, onDiscarded }: Props) {
  const uid = useId();
  const ids: Record<string, string> = Object.fromEntries(FIELD_IDS.map((f) => [f, `${uid}-${f}`]));
  const [fields, setFields] = useState<ComposerFields>(draft ? fieldsOf(draft) : EMPTY_FIELDS);
  const [current, setCurrent] = useState<DraftView | null>(draft);
  const [check, setCheck] = useState<SafetyCheck | null>(null);
  const [preview, setPreview] = useState<AllergyAssessment | null>(null);
  const [change, setChange] = useState<Change | null>(null);
  const [problems, setProblems] = useState<PrescriptionProblem[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [discarding, setDiscarding] = useState(false);
  const [discardReason, setDiscardReason] = useState("");
  const [focusNote, setFocusNote] = useState(0);
  const [savedSignature, setSavedSignature] = useState(JSON.stringify(draft ? fieldsOf(draft) : EMPTY_FIELDS));
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const checkSeq = useRef(0);
  const noteBox = useRef<HTMLTextAreaElement>(null);
  const summary = useRef<HTMLDivElement>(null);

  const dirty = JSON.stringify(fields) !== savedSignature;
  useUnsavedChangesWarning(dirty);
  useEffect(() => { if (focusNote > 0) noteBox.current?.focus(); }, [focusNote]);

  const noteChange = (s: SafetyCheck) => setChange(s.changedSinceAcknowledged ? { fingerprint: s.fingerprint, added: s.added, removed: s.removed } : null);

  // an existing draft is read with the allergy and medication lists as they stand now
  useEffect(() => {
    if (!draft) return;
    const controller = new AbortController();
    getDraft(draft.id, controller.signal).then((d) => { setCurrent(d.draft); setCheck(d.safety); noteChange(d.safety); }).catch(() => {
      if (!controller.signal.aborted) setMessage("The allergy and medication lists could not be compared just now. They are checked again when you finalize, and finalizing is refused if they cannot be.");
    });
    return () => controller.abort();
  }, [draft?.id]);                                                                                              // eslint-disable-line react-hooks/exhaustive-deps

  const assessment = check?.assessment ?? preview;
  const set = (field: keyof ComposerFields, value: string) => setFields((f) => ({ ...f, [field]: value }));
  const problemFor = (field: string) => problems.find((p) => p.field === field);
  const showNote = needsReview(assessment?.outcome) || fields.note !== "";

  /** For a draft that is not saved yet the allergy question is asked on the medication alone; a stale answer is dropped. */
  async function previewAllergy() {
    if (current || fields.medication.trim().length < 2) return;
    const seq = ++checkSeq.current;
    try {
      const p = await checkPrescription(patientId, { medication: fields.medication, doseValue: null, doseUnit: "", frequency: "", reviewNote: null });
      if (seq === checkSeq.current) setPreview(p.assessment);
    } catch {
      if (seq === checkSeq.current) setMessage("The allergy check could not be made just now. It runs again when you finalize, and finalizing is refused if it cannot run.");
    }
  }

  function fail(err: unknown) {
    setConfirming(false);
    const listed = prescriptionProblemsOf(err);
    setProblems(listed);
    const changed = safetyChangeOf(err);
    if (changed) { setChange({ fingerprint: changed.currentFingerprint, added: changed.added, removed: changed.removed }); setMessage(err instanceof ApiError ? err.message : null); return; }
    if (err instanceof ApiError && err.code === "allergy_review_required") {
      setPreview(assessmentOf(err));
      setMessage(err.message);
      setFocusNote((n) => n + 1);
    } else if (err instanceof ApiError && err.code === "validation_failed") {
      setMessage("Nothing was saved. Correct the entries marked below.");
      summary.current?.focus();
    } else if (err instanceof ApiError && err.code === "concurrency_conflict") {
      setMessage("This draft was changed by someone else since you opened it. Nothing was saved. Close it and open it again to see their changes.");
    } else if (isNetworkFailure(err)) {
      setMessage("Nothing was saved: the connection dropped. What you entered is still here; try again (the same request is safe to repeat).");
    } else {
      setMessage(`Nothing was saved: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    }
  }

  /** Saves the boxes as a draft (creating it the first time), or does nothing when nothing changed. Returns the saved draft, or null when it was refused. */
  async function persist(): Promise<DraftView | null> {
    const { entry, problems: local } = entryOf(fields);
    setMessage(null);
    if (local.length > 0) { setProblems(local); setMessage("Nothing was saved. Correct the entries marked below."); summary.current?.focus(); return null; }
    const signature = JSON.stringify(entry);
    if (current && !dirty) return current;
    setBusy(true);
    try {
      let result: DraftView;
      if (!current) {
        if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
        result = (await createDraft(patientId, attempt.current.key, entry)).draft;
        attempt.current = null;
      } else {
        result = await updateDraft(current.id, current.rowVersion, entry);
      }
      setSavedSignature(JSON.stringify(fields));
      setCurrent(result);
      setProblems([]);
      onChanged(result);
      return result;
    } catch (err) {
      fail(err);
      return null;
    } finally {
      setBusy(false);
    }
  }

  async function saveDraft() {
    const result = await persist();
    if (result) setMessage("Draft saved. Nothing is final until you finalize it.");
  }

  /** Saves, then asks the server what finalizing would meet; only a draft that is ready reaches the confirmation. */
  async function startFinalize() {
    const result = await persist();
    if (!result) return;
    setBusy(true);
    try {
      const c = await checkDraft(result.id);
      setCheck(c.safety);
      noteChange(c.safety);
      if (c.problems.length > 0) { setProblems(c.problems); setMessage("This prescription is not ready to finalize. Complete the entries marked below."); summary.current?.focus(); return; }
      if (c.safety.changedSinceAcknowledged) { setMessage("The patient's allergy or medication list changed since you last looked. Read the changes below, then continue."); return; }
      if (c.safety.reviewProblem) { setProblems([c.safety.reviewProblem]); setMessage(c.safety.reviewProblem.message); setFocusNote((n) => n + 1); return; }
      setProblems([]);
      setConfirming(true);
    } catch (err) {
      fail(err);
    } finally {
      setBusy(false);
    }
  }

  async function confirmFinalize() {
    if (!current) return;
    setBusy(true);
    try {
      const done = await finalizeDraft(current.id, current.rowVersion);
      setSavedSignature(JSON.stringify(fields));
      onFinalized(done.record, done.replayed ? "This prescription was already finalized; nothing new was recorded." : `${done.record.medication} finalized and logged.`);
    } catch (err) {
      fail(err);
    } finally {
      setBusy(false);
    }
  }

  async function acknowledge() {
    if (!current || !change) return;
    setBusy(true);
    try {
      const updated = await acknowledgeSafety(current.id, current.rowVersion, change.fingerprint);
      setCurrent(updated);
      onChanged(updated);
      const detail = await getDraft(updated.id);
      setCheck(detail.safety);
      noteChange(detail.safety);
      setMessage("You have acknowledged the allergy and medication lists as they are now.");
    } catch (err) {
      fail(err);
    } finally {
      setBusy(false);
    }
  }

  async function discard() {
    if (!current) return;
    setBusy(true);
    try {
      await discardDraft(current.id, current.rowVersion, discardReason.trim() === "" ? null : discardReason);
      setSavedSignature(JSON.stringify(fields));
      onDiscarded("The draft was discarded. It is kept in the record and cannot be finalized.");
    } catch (err) {
      fail(err);
    } finally {
      setBusy(false);
    }
  }

  const error = (field: string) => { const p = problemFor(field); return p ? <span id={`${ids[field]}-error`} className="alv-rx__error">{p.message}</span> : null; };
  const described = (field: string) => (problemFor(field) ? `${ids[field]}-error` : undefined);
  const invalid = (field: string) => (problemFor(field) ? true : undefined);
  const jump = (field: string) => document.getElementById(ids[field])?.focus();

  if (confirming && current) {
    return (
      <div className="alv-rx__layout">
        <FinalizeConfirmation draft={current} assessment={assessment} busy={busy} limitation={options.decisionSupportMessage} onConfirm={() => void confirmFinalize()} onBack={() => setConfirming(false)} message={message} />
        <PrescriptionSafetyPanel safety={safety} assessment={assessment} doseLimitation={options.doseLimitation} allergyLimitation={options.allergyLimitation} />
      </div>
    );
  }

  return (
    <div className="alv-rx__layout">
      <form className="alv-rx__form" aria-label="Prescription composer" noValidate onSubmit={(e) => { e.preventDefault(); void saveDraft(); }}>
        <h3>{current ? (current.supersedesPrescriptionId ? "Amend a prescription (draft)" : "Prescription draft") : "New prescription draft"}</h3>
        {current?.supersedesPrescriptionId && <p className="alv-clinical__meta">Finalizing this draft replaces the prescription it amends; until then the original stays active and unchanged.</p>}
        <div ref={summary} tabIndex={-1} role={problems.length > 0 || message ? "alert" : undefined} className={problems.length > 0 ? "alv-rx__problems" : "alv-rx__message"}>
          {message && <p>{message}</p>}
          {problems.length > 0 && (
            <ul aria-label="Entries to correct">{problems.map((p) => (
              <li key={`${p.field}-${p.code}`}><button type="button" className="alv-rx__jump" onClick={() => jump(p.field)}>{p.message}</button></li>
            ))}</ul>
          )}
        </div>

        {change && (
          <div className="alv-rx__changed" role="alert" aria-labelledby={`${uid}-changed`}>
            <h4 id={`${uid}-changed`}>The allergy or medication list changed since you last looked</h4>
            {change.added.length > 0 && <><p>Added or changed:</p><ul aria-label="Added or changed">{change.added.map((a) => <li key={a}>{a}</li>)}</ul></>}
            {change.removed.length > 0 && <><p>No longer listed as it was:</p><ul aria-label="No longer listed">{change.removed.map((a) => <li key={a}>{a}</li>)}</ul></>}
            <button type="button" className="btn btn-outline-primary" disabled={busy || !current} onClick={() => void acknowledge()}>I have read the changes</button>
          </div>
        )}

        <label htmlFor={ids.medication}>Medication</label>
        <input id={ids.medication} type="text" maxLength={options.medicationMax} value={fields.medication} aria-invalid={invalid("medication")} aria-describedby={described("medication")}
          onChange={(e) => set("medication", e.target.value)} onBlur={() => void previewAllergy()} autoComplete="off" />
        {error("medication")}

        <div className="alv-rx__dose">
          <div>
            <label htmlFor={ids.doseValue}>Dose</label>
            <input id={ids.doseValue} type="text" inputMode="decimal" value={fields.dose} aria-invalid={invalid("doseValue")} aria-describedby={described("doseValue")} onChange={(e) => set("dose", e.target.value)} />
            {error("doseValue")}
          </div>
          <div>
            <label htmlFor={ids.doseUnit}>Unit</label>
            <select id={ids.doseUnit} value={fields.unit} aria-invalid={invalid("doseUnit")} aria-describedby={described("doseUnit")} onChange={(e) => set("unit", e.target.value)}>
              <option value="">Choose a unit</option>
              {options.doseUnits.map((u) => <option key={u} value={u}>{u}</option>)}
            </select>
            {error("doseUnit")}
          </div>
        </div>
        <p className="alv-clinical__meta">{options.doseLimitation}</p>

        <label htmlFor={ids.frequency}>How often</label>
        <select id={ids.frequency} value={fields.frequency} aria-invalid={invalid("frequency")} aria-describedby={described("frequency")} onChange={(e) => set("frequency", e.target.value)}>
          <option value="">Choose how often</option>
          {options.frequencies.map((f) => <option key={f} value={f}>{f}</option>)}
        </select>
        {error("frequency")}

        <label htmlFor={ids.instructions}>Instructions for the patient</label>
        <textarea id={ids.instructions} rows={3} maxLength={options.instructionsMax} value={fields.instructions} aria-invalid={invalid("instructions")} aria-describedby={described("instructions")} onChange={(e) => set("instructions", e.target.value)} />
        {error("instructions")}

        <div className="alv-rx__dose">
          <div>
            <label htmlFor={ids.quantityValue}>Quantity</label>
            <input id={ids.quantityValue} type="text" inputMode="decimal" value={fields.quantity} aria-invalid={invalid("quantityValue")} aria-describedby={described("quantityValue")} onChange={(e) => set("quantity", e.target.value)} />
            {error("quantityValue")}
          </div>
          <div>
            <label htmlFor={ids.quantityUnit}>Quantity unit</label>
            <select id={ids.quantityUnit} value={fields.quantityUnit} aria-invalid={invalid("quantityUnit")} aria-describedby={described("quantityUnit")} onChange={(e) => set("quantityUnit", e.target.value)}>
              <option value="">Choose a unit</option>
              {options.quantityUnits.map((u) => <option key={u} value={u}>{u}</option>)}
            </select>
            {error("quantityUnit")}
          </div>
        </div>
        <label htmlFor={ids.refills}>Refills</label>
        <input id={ids.refills} type="text" inputMode="numeric" value={fields.refills} aria-invalid={invalid("refills")} aria-describedby={described("refills")} onChange={(e) => set("refills", e.target.value)} />
        {error("refills")}
        <p className="alv-clinical__meta">{options.quantityLimitation}</p>

        {showNote && (
          <>
            <label htmlFor={ids.reviewNote}>{reviewLabel(assessment?.outcome)}</label>
            <textarea id={ids.reviewNote} ref={noteBox} rows={3} maxLength={500} value={fields.note} aria-invalid={invalid("reviewNote")} aria-describedby={described("reviewNote")} onChange={(e) => set("note", e.target.value)} />
            {error("reviewNote")}
          </>
        )}

        {discarding && (
          <>
            <label htmlFor={`${uid}-discard`}>Why are you discarding this draft? (optional, kept with it)</label>
            <textarea id={`${uid}-discard`} rows={2} maxLength={500} value={discardReason} onChange={(e) => setDiscardReason(e.target.value)} />
          </>
        )}

        <div className="alv-clinical__row-actions">
          <button type="submit" className="btn btn-primary" disabled={busy}>Save draft</button>
          <button type="button" className="btn btn-outline-primary" disabled={busy} onClick={() => void startFinalize()}>Finalize…</button>
          {current && !discarding && <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => setDiscarding(true)}>Discard draft</button>}
          {current && discarding && <button type="button" className="btn btn-outline-danger" disabled={busy} onClick={() => void discard()}>Confirm discard</button>}
          {discarding && <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => setDiscarding(false)}>Keep the draft</button>}
          <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={onClosed}>Close</button>
        </div>
      </form>
      <PrescriptionSafetyPanel safety={safety} assessment={assessment} doseLimitation={options.doseLimitation} allergyLimitation={options.allergyLimitation} />
    </div>
  );
}
