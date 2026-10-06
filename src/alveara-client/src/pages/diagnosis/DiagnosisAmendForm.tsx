import { useId, useRef, useState } from "react";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { amendDiagnosis, diagnosisProblemsOf } from "../../services/diagnosisApi";
import type { Diagnosis, DiagnosisProblem } from "../../services/diagnosisApi";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { StructureFields, ToothSelect } from "./DiagnosisStructureFields";
import { checkStructure, structureInputFromDraft } from "./diagnosisStructureRules";
import type { StructureDraft } from "./diagnosisStructureRules";

const FIELDS = ["toothKey", "regionKey", "codingSystem", "code", "source", "sourceNote", "reason"] as const;
const LABELS: Record<string, string> = { toothKey: "Tooth", regionKey: "Oral region", codingSystem: "Coding system", code: "Code", source: "Source", sourceNote: "Source note", reason: "Reason" };

interface Props {
  diagnosis: Diagnosis;
  numbering: NumberingSystem;
  /** Called with the diagnosis as the server now holds it, after an amendment. */
  onSaved: (diagnosis: Diagnosis, text: string) => void;
  onConflict: () => void;
  onCancel: () => void;
}

/**
 * ALV-013-C01: amend the STRUCTURE of a diagnosis: where it is (tooth or region), its coding and its source. The boxes start with what the diagnosis has now; what is saved is the whole structure as it
 * should now stand, with the reason. The earlier values stay in the history with who, when and why, and the diagnosis's label, notes and treatment-plan reference are not touched (the reference stays
 * unresolved). Entries are checked with the server's rules before anything is sent; every problem is listed and linked, and everything typed is kept through a refusal, a stale change or a dropped connection.
 */
export function DiagnosisAmendForm({ diagnosis: d, numbering, onSaved, onConflict, onCancel }: Props) {
  const uid = useId();
  const ids: Record<string, string> = Object.fromEntries(FIELDS.map((f) => [f, `${uid}-${f}`]));
  const [tooth, setTooth] = useState(d.toothKey ?? "");
  const [structure, setStructure] = useState<StructureDraft>({
    codingSystem: d.codingSystem ?? "", code: d.code ?? "", source: d.source && d.source !== "Manual" ? d.source : "", sourceNote: d.sourceNote ?? "", regionKey: d.regionKey ?? "",
  });
  const [reason, setReason] = useState("");
  const [problems, setProblems] = useState<DiagnosisProblem[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const summary = useRef<HTMLDivElement>(null);

  const bad = (field: string) => problems.filter((p) => p.field === field);
  const helpers = { ids, invalid: (f: string) => (bad(f).length > 0 || undefined), describe: (f: string) => (bad(f).length > 0 ? `${ids[f]}-problem` : undefined) };

  function refuse(found: DiagnosisProblem[], text: string) {
    setProblems(found);
    setMessage(text);
    setTimeout(() => summary.current?.focus(), 0);
  }

  async function save() {
    setProblems([]);
    setMessage(null);
    const check = checkStructure(structureInputFromDraft(tooth, structure));
    const found = [...check.problems];
    if (reason.trim() === "") found.push({ field: "reason", code: "required", message: "Say why this diagnosis is being amended." });
    if (found.length > 0) return refuse(found, "Not saved: some entries need correcting. Nothing was sent.");
    const s = check.value!;
    setBusy(true);
    try {
      const saved = await amendDiagnosis(d, { toothKey: tooth === "" ? null : tooth, regionKey: s.regionKey, codingSystem: s.codingSystem, code: s.code, source: s.source === "Manual" ? null : s.source, sourceNote: s.sourceNote, reason: reason.trim() });
      onSaved(saved, "Diagnosis amended.");
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

  return (
    <form className="alv-dx__form" aria-label="Amend this diagnosis" onSubmit={(e) => { e.preventDefault(); void save(); }}>
      <p className="alv-clinical__meta">An amendment changes where the diagnosis is, its coding or its source. The earlier values stay in the history with who changed them and why. The label, the notes and the treatment plan reference are not changed here.</p>
      {message && <p className="alv-dx__message" role="status">{message}</p>}
      {problems.length > 0 && (
        <div className="alv-dx__problems" role="alert" tabIndex={-1} ref={summary}>
          <h3>Some entries need correcting</h3>
          <ul>{problems.map((p, i) => <li key={`${p.field}${p.code}${i}`}>{ids[p.field] ? <button type="button" className="alv-dx__jump" onClick={() => document.getElementById(ids[p.field])?.focus()}>{LABELS[p.field] ?? p.field}</button> : <strong>{p.field}</strong>}: {p.message}</li>)}</ul>
        </div>
      )}
      <ToothSelect value={tooth} numbering={numbering} onChange={(k) => { setTooth(k); if (k) setStructure((x) => ({ ...x, regionKey: "" })); setMessage(null); }} {...helpers} />
      <StructureFields value={structure} onChange={(patch) => { setStructure((x) => ({ ...x, ...patch, ...(patch.source === "" ? { sourceNote: "" } : {}) })); if (patch.regionKey) setTooth(""); setMessage(null); }} {...helpers} />
      <label htmlFor={ids.reason}>Why is this being amended?
        <input id={ids.reason} type="text" maxLength={500} value={reason} onChange={(e) => { setReason(e.target.value); setMessage(null); }} aria-invalid={helpers.invalid("reason")} aria-describedby={helpers.describe("reason")} />
      </label>
      {FIELDS.flatMap((f) => bad(f).map((p, i) => <span key={`${f}${i}`} id={`${ids[f]}-problem`} className="alv-dx__sr">{p.message}</span>))}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Save amendment</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
