import { useEffect, useId, useMemo, useRef, useState } from "react";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError } from "../../services/authApi";
import { isNetworkFailure, newKey } from "../../services/clinicalApi";
import { assessmentOf, checkPrescription, prescriptionProblemsOf, savePrescription } from "../../services/prescriptionApi";
import type { AllergyAssessment, PrescriptionEntryInput, PrescriptionOptions, PrescriptionProblem, SavedPrescription } from "../../services/prescriptionApi";
import { needsReview, reviewLabel } from "./prescriptionText";

interface Draft { medication: string; dose: string; unit: string; frequency: string; note: string }
const EMPTY: Draft = { medication: "", dose: "", unit: "", frequency: "", note: "" };
const FIELDS = ["medication", "doseValue", "doseUnit", "frequency", "reviewNote"] as const;

interface Props {
  patientId: string;
  options: PrescriptionOptions;
  /** What the allergy check last said about the entry (null before one was made); the safety panel shows it. */
  assessment: AllergyAssessment | null;
  onAssessment: (assessment: AllergyAssessment | null) => void;
  onSaved: (saved: SavedPrescription, text: string) => void;
}

/** The dose box as a number: empty is "not given", digits with an optional decimal part are a number, anything else is a problem to put right (nothing is sent). */
export function parseDose(text: string): { value: number | null; problem: PrescriptionProblem | null } {
  const t = text.trim();
  if (t === "") return { value: null, problem: null };
  if (!/^\d+(\.\d+)?$/.test(t)) return { value: null, problem: { field: "doseValue", code: "not_a_number", message: "Enter the dose as a number, for example 500 or 2.5." } };
  return { value: Number(t), problem: null };
}

/**
 * STORY-016: the prescription form. It checks the allergy question as soon as a medication is entered (a name comparison against the patient's recorded allergies, shown in the safety panel), names every
 * problem beside its box when the server refuses an entry, and when a matching allergy or an unestablished allergy list needs the prescriber's own words it shows that box with the reason and saves nothing
 * until it is filled in. Everything typed is kept through a refusal and through a dropped connection; each attempt carries a save key that is kept while the entry is unchanged, so a retry or a double click
 * cannot make two prescriptions. Dose checking here is structural, and the form says what that does not cover.
 */
export function PrescriptionForm({ patientId, options, assessment, onAssessment, onSaved }: Props) {
  const uid = useId();
  const ids: Record<string, string> = Object.fromEntries(FIELDS.map((f) => [f, `${uid}-${f}`]));
  const [draft, setDraft] = useState<Draft>(EMPTY);
  const [problems, setProblems] = useState<PrescriptionProblem[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [focusNote, setFocusNote] = useState(0);
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const checkSeq = useRef(0);
  const noteBox = useRef<HTMLTextAreaElement>(null);
  const summary = useRef<HTMLDivElement>(null);

  const dirty = useMemo(() => JSON.stringify(draft) !== JSON.stringify(EMPTY), [draft]);
  useUnsavedChangesWarning(dirty);

  const showNote = needsReview(assessment?.outcome) || draft.note !== "";
  useEffect(() => { if (focusNote > 0) noteBox.current?.focus(); }, [focusNote]);

  const set = (field: keyof Draft, value: string) => setDraft((d) => ({ ...d, [field]: value }));
  const entryOf = (d: Draft, dose: number | null): PrescriptionEntryInput => ({ medication: d.medication, doseValue: dose, doseUnit: d.unit, frequency: d.frequency, reviewNote: d.note.trim() === "" ? null : d.note });
  const problemFor = (field: string) => problems.find((p) => p.field === field);

  /** Asks the server what the allergy check says about the medication typed so far; a stale answer (the entry changed meanwhile) is dropped. */
  async function checkAllergy(d: Draft): Promise<void> {
    if (d.medication.trim().length < 2) { onAssessment(null); return; }
    const seq = ++checkSeq.current;
    try {
      const preview = await checkPrescription(patientId, entryOf(d, parseDose(d.dose).value));
      if (seq === checkSeq.current) onAssessment(preview.assessment);
    } catch {
      if (seq === checkSeq.current) setMessage("The allergy check could not be made just now. It runs again when you save, and a save is refused if it cannot run.");
    }
  }

  async function checkEntry() {
    const dose = parseDose(draft.dose);
    setMessage(null);
    setBusy(true);
    try {
      const preview = await checkPrescription(patientId, entryOf(draft, dose.value));
      onAssessment(preview.assessment);
      setProblems([...(dose.problem ? [dose.problem] : []), ...preview.problems.filter((p) => !(dose.problem && p.field === "doseValue"))]);
      setMessage(preview.problems.length === 0 && !dose.problem ? "The entry has no structural problems. Nothing has been saved." : "Nothing has been saved. Correct the entries marked below.");
    } catch (err) {
      setMessage(err instanceof ApiError ? `The entry could not be checked: ${err.message}` : "The entry could not be checked: the connection dropped. Try again.");
    } finally {
      setBusy(false);
    }
  }

  async function save() {
    const dose = parseDose(draft.dose);
    setMessage(null);
    if (dose.problem) { setProblems([dose.problem]); setMessage("Nothing was saved. Correct the entries marked below."); summary.current?.focus(); return; }
    const entry = entryOf(draft, dose.value);
    const signature = JSON.stringify(entry);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setBusy(true);
    try {
      const saved = await savePrescription(patientId, attempt.current.key, entry);
      setDraft(EMPTY);
      setProblems([]);
      attempt.current = null;
      onAssessment(null);
      onSaved(saved, saved.replayed ? "This prescription was already saved; nothing new was recorded." : `${saved.prescription.medication} prescribed and logged.`);
    } catch (err) {
      const listed = prescriptionProblemsOf(err);
      setProblems(listed);
      if (err instanceof ApiError && err.code === "allergy_review_required") {
        onAssessment(assessmentOf(err));
        setMessage(err.message);                                                                                // the server's sentence already says nothing was saved
        setFocusNote((n) => n + 1);
      } else if (err instanceof ApiError && err.code === "validation_failed") {
        setMessage("Nothing was saved. Correct the entries marked below.");
        summary.current?.focus();
      } else if (isNetworkFailure(err)) {
        setMessage("Nothing was saved: the connection dropped. What you entered is still here; try again (the same request is safe to repeat).");
      } else {
        setMessage(`Nothing was saved: ${err instanceof ApiError ? err.message : "something went wrong."}`);
      }
    } finally {
      setBusy(false);
    }
  }

  const error = (field: string) => { const p = problemFor(field); return p ? <span id={`${ids[field]}-error`} className="alv-rx__error">{p.message}</span> : null; };
  const described = (field: string) => (problemFor(field) ? `${ids[field]}-error` : undefined);
  const invalid = (field: string) => (problemFor(field) ? true : undefined);
  const jump = (field: string) => document.getElementById(ids[field])?.focus();

  return (
    <form className="alv-rx__form" aria-label="Prescribe a medication" noValidate onSubmit={(e) => { e.preventDefault(); void save(); }}>
      <h3>Prescribe a medication</h3>
      <div ref={summary} tabIndex={-1} role={problems.length > 0 || message ? "alert" : undefined} className={problems.length > 0 ? "alv-rx__problems" : "alv-rx__message"}>
        {message && <p>{message}</p>}
        {problems.length > 0 && (
          <ul aria-label="Entries to correct">{problems.map((p) => (
            <li key={`${p.field}-${p.code}`}><button type="button" className="alv-rx__jump" onClick={() => jump(p.field)}>{p.message}</button></li>
          ))}</ul>
        )}
      </div>

      <label htmlFor={ids.medication}>Medication</label>
      <input id={ids.medication} type="text" maxLength={options.medicationMax} value={draft.medication} aria-invalid={invalid("medication")} aria-describedby={described("medication")}
        onChange={(e) => set("medication", e.target.value)} onBlur={() => void checkAllergy(draft)} autoComplete="off" />
      {error("medication")}

      <div className="alv-rx__dose">
        <div>
          <label htmlFor={ids.doseValue}>Dose</label>
          <input id={ids.doseValue} type="text" inputMode="decimal" value={draft.dose} aria-invalid={invalid("doseValue")} aria-describedby={described("doseValue")} onChange={(e) => set("dose", e.target.value)} />
          {error("doseValue")}
        </div>
        <div>
          <label htmlFor={ids.doseUnit}>Unit</label>
          <select id={ids.doseUnit} value={draft.unit} aria-invalid={invalid("doseUnit")} aria-describedby={described("doseUnit")} onChange={(e) => set("unit", e.target.value)}>
            <option value="">Choose a unit</option>
            {options.doseUnits.map((u) => <option key={u} value={u}>{u}</option>)}
          </select>
          {error("doseUnit")}
        </div>
      </div>
      <p className="alv-clinical__meta">{options.doseLimitation}</p>

      <label htmlFor={ids.frequency}>How often</label>
      <select id={ids.frequency} value={draft.frequency} aria-invalid={invalid("frequency")} aria-describedby={described("frequency")} onChange={(e) => set("frequency", e.target.value)}>
        <option value="">Choose how often</option>
        {options.frequencies.map((f) => <option key={f} value={f}>{f}</option>)}
      </select>
      {error("frequency")}

      {showNote && (
        <>
          <label htmlFor={ids.reviewNote}>{reviewLabel(assessment?.outcome)}</label>
          <textarea id={ids.reviewNote} ref={noteBox} rows={3} maxLength={options.reviewNoteMax} value={draft.note} aria-invalid={invalid("reviewNote")} aria-describedby={described("reviewNote")} onChange={(e) => set("note", e.target.value)} />
          {error("reviewNote")}
        </>
      )}

      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Save prescription</button>
        <button type="button" className="btn btn-outline-primary" disabled={busy} onClick={() => void checkEntry()}>Check entry</button>
      </div>
    </form>
  );
}
