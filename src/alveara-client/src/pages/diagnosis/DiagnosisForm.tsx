import { useId, useMemo, useRef, useState } from "react";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure, newKey } from "../../services/clinicalApi";
import type { EncounterSummary } from "../../services/clinicalApi";
import { correctDiagnosis, diagnosisProblemsOf, recordDiagnosis } from "../../services/diagnosisApi";
import type { Diagnosis, DiagnosisProblem } from "../../services/diagnosisApi";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { StructureFields, ToothSelect } from "./DiagnosisStructureFields";
import { EMPTY_STRUCTURE, checkStructure, structureInputFromDraft } from "./diagnosisStructureRules";
import type { StructureDraft } from "./diagnosisStructureRules";
import { LABEL_MAX, NOTES_MAX, REFERENCE_MAX, checkDiagnosis, entryFromDraft } from "./diagnosisRules";
import type { DiagnosisDraft } from "./diagnosisRules";

const FIELDS = ["encounterId", "label", "toothKey", "regionKey", "codingSystem", "code", "source", "sourceNote", "notes", "treatmentPlanReference", "reason"] as const;

type PlanAction = "keep" | "replace" | "clear";

interface Props {
  patientId: string;
  numbering: NumberingSystem;
  encounters: EncounterSummary[];
  /** Present when correcting this diagnosis; absent when recording a new one. */
  correcting?: Diagnosis;
  /** Called with the diagnosis as the server now holds it, after a save. */
  onSaved: (diagnosis: Diagnosis, text: string) => void;
  /** A stale correction: the parent shows the conflict banner and reloads. */
  onConflict: () => void;
  onCancel?: () => void;
}

/**
 * STORY-013: the diagnosis form, for recording a new diagnosis and for correcting one. Entries are checked before anything is sent with the same rules the server applies; every problem is listed in one
 * place with a link to its box and nothing is sent until they are put right; if the server refuses, it says what to correct the same way. Everything typed is kept through a refusal, a stale change and a
 * dropped connection. Each recording attempt carries a key that is kept while the entry is unchanged (a retry or double click cannot make two diagnoses) and renewed when anything changes.
 *
 * ALV-013-C01: when recording, the form also takes the oral region (instead of a tooth), optional coding and the source; all optional, and checked with the same rules as the server.
 *
 * The treatment-plan reference is a FORWARD reference: the form says it is a note of a plan to link later, not proof that a plan exists. When correcting, it is kept as it is unless the person chooses to
 * replace or remove it, and both choices ask for the reason that every correction needs.
 */
export function DiagnosisForm({ patientId, numbering, encounters, correcting, onSaved, onConflict, onCancel }: Props) {
  const [initial] = useState<DiagnosisDraft>(() => correcting
    ? { encounterId: correcting.encounterId, label: correcting.label, toothKey: correcting.toothKey ?? "", notes: correcting.notes ?? "", treatmentPlanReference: "" }
    : { encounterId: encounters[0]?.id ?? "", label: "", toothKey: "", notes: "", treatmentPlanReference: "" });
  const uid = useId();
  const ids: Record<string, string> = Object.fromEntries(FIELDS.map((f) => [f, `${uid}-${f}`]));
  const [draft, setDraft] = useState<DiagnosisDraft>(initial);
  const [structure, setStructure] = useState<StructureDraft>(EMPTY_STRUCTURE);
  const [planAction, setPlanAction] = useState<PlanAction>("keep");
  const [reason, setReason] = useState("");
  const [problems, setProblems] = useState<DiagnosisProblem[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const summary = useRef<HTMLDivElement>(null);

  const dirty = useMemo(() => JSON.stringify(draft) !== JSON.stringify(initial) || JSON.stringify(structure) !== JSON.stringify(EMPTY_STRUCTURE) || reason !== "" || planAction !== "keep", [draft, structure, reason, planAction, initial]);
  useUnsavedChangesWarning(dirty && !correcting);

  // a diagnosis is about one tooth or one region: choosing one clears the other
  const set = (patch: Partial<DiagnosisDraft>) => {
    setDraft((d) => ({ ...d, ...patch }));
    if (patch.toothKey) setStructure((x) => ({ ...x, regionKey: "" }));
    setMessage(null);
  };
  const setStruct = (patch: Partial<StructureDraft>) => {
    setStructure((x) => ({ ...x, ...patch, ...(patch.source === "" ? { sourceNote: "" } : {}) }));
    if (patch.regionKey) setDraft((d) => ({ ...d, toothKey: "" }));
    setMessage(null);
  };

  function refuse(found: DiagnosisProblem[], text: string) {
    setProblems(found);
    setMessage(text);
    setTimeout(() => summary.current?.focus(), 0);
  }

  async function save() {
    setProblems([]);
    setMessage(null);
    // the draft as the rules judge it: when correcting, the reference box is only read if the person chose to replace it
    const entry = entryFromDraft(correcting && planAction !== "replace" ? { ...draft, treatmentPlanReference: "" } : draft);
    const check = checkDiagnosis(entry);
    const found = [...check.problems];
    const struct = correcting ? null : checkStructure(structureInputFromDraft(draft.toothKey, structure));
    if (struct) found.push(...struct.problems);
    if (correcting && reason.trim() === "") found.push({ field: "reason", code: "required", message: "Say why this diagnosis is being corrected." });
    if (found.length > 0) return refuse(found, "Not saved: some entries need correcting. Nothing was sent.");
    const value = check.value!;
    const parts = struct?.value;

    setBusy(true);
    try {
      if (correcting) {
        const saved = await correctDiagnosis(correcting, {
          label: value.label, toothKey: value.toothKey, notes: value.notes, treatmentPlanReference: planAction === "replace" ? value.treatmentPlanReference : null,
          clearTreatmentPlanReference: planAction === "clear", reason: reason.trim(),
        });
        onSaved(saved, "Diagnosis corrected.");
      } else {
        const signature = JSON.stringify([value, parts]);
        if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
        const saved = await recordDiagnosis(patientId, attempt.current.key, { ...value, codingSystem: parts!.codingSystem, code: parts!.code, source: parts!.source === "Manual" ? null : parts!.source, sourceNote: parts!.sourceNote, regionKey: parts!.regionKey });
        attempt.current = null;
        setDraft({ ...initial, encounterId: draft.encounterId });
        setStructure(EMPTY_STRUCTURE);
        onSaved(saved, "Diagnosis recorded.");
      }
    } catch (err) {
      const server = diagnosisProblemsOf(err);
      if (isConcurrencyConflict(err)) { setMessage("Not saved: someone else changed this diagnosis. What you entered is still here."); onConflict(); }
      else if (err instanceof ApiError && server.length > 0) refuse(server, "Not saved: some entries need correcting. Everything you entered is still here.");
      else if (isNetworkFailure(err)) setMessage("Not saved: the connection dropped. What you entered is still here; save again to retry.");
      else setMessage(`Not saved: ${err instanceof ApiError ? err.message : "something went wrong."} What you entered is still here.`);
    } finally {
      setBusy(false);
    }
  }

  const bad = (field: string) => problems.filter((p) => p.field === field);
  const helpers = { ids, invalid: (field: string) => (bad(field).length > 0 || undefined), describe: (field: string) => (bad(field).length > 0 ? `${ids[field]}-problem` : undefined) };
  const describe = (field: string) => (bad(field).length > 0 ? `${ids[field]}-problem` : undefined);
  const jump = (field: string) => document.getElementById(ids[field])?.focus();

  return (
    <form className="alv-dx__form" aria-label={correcting ? "Correct this diagnosis" : "Record a diagnosis"} onSubmit={(e) => { e.preventDefault(); void save(); }}>
      {message && <p className="alv-dx__message" role="status">{message}</p>}
      {problems.length > 0 && (
        <div className="alv-dx__problems" role="alert" tabIndex={-1} ref={summary}>
          <h3>Some entries need correcting</h3>
          <ul>{problems.map((p, i) => <li key={`${p.field}${p.code}${i}`}>{ids[p.field] ? <button type="button" className="alv-dx__jump" onClick={() => jump(p.field)}>{FIELD_LABELS[p.field] ?? p.field}</button> : <strong>{p.field}</strong>}: {p.message}</li>)}</ul>
        </div>
      )}

      {!correcting && (
        <label htmlFor={ids.encounterId}>Encounter
          <select id={ids.encounterId} value={draft.encounterId} onChange={(e) => set({ encounterId: e.target.value })} aria-invalid={bad("encounterId").length > 0 || undefined} aria-describedby={describe("encounterId")}>
            <option value="">Choose an encounter</option>
            {encounters.map((e) => <option key={e.id} value={e.id}>{new Date(e.encounterAtUtc).toLocaleString()} ({e.status.toLowerCase()})</option>)}
          </select>
        </label>
      )}
      {correcting && <p className="alv-clinical__meta">Encounter of {new Date(correcting.encounterAtUtc).toLocaleString()}. The patient and the encounter never change; to record against another encounter, withdraw this one and record a new diagnosis.</p>}

      <label htmlFor={ids.label}>Diagnosis
        <input id={ids.label} type="text" maxLength={LABEL_MAX + 50} value={draft.label} onChange={(e) => set({ label: e.target.value })} aria-invalid={bad("label").length > 0 || undefined} aria-describedby={describe("label")} />
      </label>
      <ToothSelect value={draft.toothKey} numbering={numbering} onChange={(k) => set({ toothKey: k })} {...helpers} />
      {!correcting && <StructureFields value={structure} onChange={setStruct} {...helpers} />}
      <label htmlFor={ids.notes}>Notes (optional)
        <textarea id={ids.notes} rows={3} value={draft.notes} onChange={(e) => set({ notes: e.target.value })} aria-invalid={bad("notes").length > 0 || undefined} aria-describedby={describe("notes")} />
        <span className="alv-clinical__meta">{draft.notes.length} of {NOTES_MAX} characters</span>
      </label>

      <fieldset className="alv-dx__plan">
        <legend>Treatment plan reference (optional, unresolved)</legend>
        <p className="alv-clinical__meta">A note of the treatment plan this diagnosis belongs to, to be linked when treatment plans exist. It is kept as text only: it does not mean a plan with this reference exists, and nothing checks it.</p>
        {correcting && (
          <div role="radiogroup" aria-label="What to do with the treatment plan reference">
            <label><input type="radio" name="alv-dx-plan-action" checked={planAction === "keep"} onChange={() => setPlanAction("keep")} /> Keep it{correcting.treatmentPlanReference ? <> (<strong>{correcting.treatmentPlanReference}</strong>)</> : " (there is none)"}</label>
            <label><input type="radio" name="alv-dx-plan-action" checked={planAction === "replace"} onChange={() => setPlanAction("replace")} /> {correcting.treatmentPlanReference ? "Replace it" : "Add one"}</label>
            {correcting.treatmentPlanReference && <label><input type="radio" name="alv-dx-plan-action" checked={planAction === "clear"} onChange={() => setPlanAction("clear")} /> Remove it</label>}
          </div>
        )}
        {(!correcting || planAction === "replace") && (
          <label htmlFor={ids.treatmentPlanReference}>Reference
            <input id={ids.treatmentPlanReference} type="text" maxLength={REFERENCE_MAX + 50} value={draft.treatmentPlanReference} onChange={(e) => set({ treatmentPlanReference: e.target.value })} aria-invalid={bad("treatmentPlanReference").length > 0 || undefined} aria-describedby={describe("treatmentPlanReference")} />
          </label>
        )}
      </fieldset>

      {correcting && (
        <label htmlFor={ids.reason}>Why is this being corrected?
          <input id={ids.reason} type="text" maxLength={500} value={reason} onChange={(e) => { setReason(e.target.value); setMessage(null); }} aria-invalid={bad("reason").length > 0 || undefined} aria-describedby={describe("reason")} />
        </label>
      )}
      {correcting && planAction !== "keep" && <p className="alv-clinical__meta">Changing the treatment plan reference is recorded in the history with this reason.</p>}
      {FIELDS.flatMap((f) => bad(f).map((p, i) => <span key={`${f}${i}`} id={`${ids[f]}-problem`} className="alv-dx__sr">{p.message}</span>))}

      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>{correcting ? "Save correction" : "Record diagnosis"}</button>
        {onCancel && <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>}
      </div>
    </form>
  );
}

const FIELD_LABELS: Record<string, string> = {
  encounterId: "Encounter", label: "Diagnosis", toothKey: "Tooth", regionKey: "Oral region", codingSystem: "Coding system", code: "Code", source: "Source", sourceNote: "Source note",
  notes: "Notes", treatmentPlanReference: "Treatment plan reference", reason: "Reason",
};
