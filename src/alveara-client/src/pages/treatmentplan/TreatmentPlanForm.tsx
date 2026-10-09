import { useId, useRef, useState } from "react";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure, newKey } from "../../services/clinicalApi";
import type { Diagnosis } from "../../services/diagnosisApi";
import { createTreatmentPlan, planFieldErrorsOf } from "../../services/treatmentPlansApi";
import type { PlanningProcedure, TreatmentPlan } from "../../services/treatmentPlansApi";
import { TreatmentPlanItemFields } from "./TreatmentPlanItemFields";
import { EMPTY_ITEM, itemGap, otherMessages, titleGap, toItemInput } from "./treatmentPlanRules";
import type { ItemDraft } from "./treatmentPlanRules";

interface Props {
  patientId: string;
  diagnoses: Diagnosis[];
  procedures: PlanningProcedure[];
  onSaved: (plan: TreatmentPlan) => void;
}

const KNOWN = ["title", "items[0].diagnosisId", "items[0].procedureId", "items[0].toothKey", "items[0].surface"] as const;

/**
 * STORY-015: start a treatment plan from one of the patient's current diagnoses. A plan needs a title and at least one proposed procedure (more are added afterwards); each procedure's fee is the catalog
 * fee in effect when the plan is saved. A gap is named beside its field before anything is sent; if the server refuses, it says what to correct the same way. Everything typed is kept through a refusal
 * and a dropped connection. Each attempt carries a key that is kept while the entry is unchanged (a retry or double click cannot make two plans) and renewed when anything changes.
 */
export function TreatmentPlanForm({ patientId, diagnoses, procedures, onSaved }: Props) {
  const uid = useId();
  const [title, setTitle] = useState("");
  const [item, setItem] = useState<ItemDraft>(EMPTY_ITEM);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const summary = useRef<HTMLDivElement>(null);

  function refuse(found: Record<string, string>, text: string) {
    setErrors(found);
    setMessage(text);
    setTimeout(() => summary.current?.focus(), 0);
  }

  async function save() {
    setErrors({});
    setMessage(null);
    const found: Record<string, string> = {};
    const titleProblem = titleGap(title);
    if (titleProblem) found.title = titleProblem;
    const gap = itemGap(item);
    if (gap) found[!item.diagnosisId ? "items[0].diagnosisId" : !item.procedure ? "items[0].procedureId" : "items[0].toothKey"] = gap;
    if (Object.keys(found).length > 0) return refuse(found, "Not saved: some entries need correcting. Nothing was sent.");

    const input = toItemInput(item);
    const signature = JSON.stringify([title.trim(), input]);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setBusy(true);
    try {
      const plan = await createTreatmentPlan(patientId, attempt.current.key, title.trim(), [input]);
      attempt.current = null;
      setTitle("");
      setItem(EMPTY_ITEM);
      onSaved(plan);
    } catch (err) {
      const server = planFieldErrorsOf(err);
      if (isConcurrencyConflict(err)) setMessage("Not saved: something changed while you were working. What you entered is still here.");
      else if (err instanceof ApiError && Object.keys(server).length > 0) refuse(server, "Not saved: some entries need correcting. Everything you entered is still here.");
      else if (isNetworkFailure(err)) setMessage("Not saved: the connection dropped. What you entered is still here; save again to retry.");
      else setMessage(`Not saved: ${err instanceof ApiError ? err.message : "something went wrong."} What you entered is still here.`);
    } finally {
      setBusy(false);
    }
  }

  const rest = otherMessages(errors, KNOWN);
  return (
    <form className="alv-dx__form" aria-label="Create a treatment plan" onSubmit={(e) => { e.preventDefault(); void save(); }}>
      {message && <p className="alv-dx__message" role="status">{message}</p>}
      {Object.keys(errors).length > 0 && (
        <div className="alv-dx__problems" role="alert" tabIndex={-1} ref={summary}>
          <h3>Some entries need correcting</h3>
          <ul>{Object.values(errors).map((m, i) => <li key={`${i}-${m}`}>{m}</li>)}</ul>
        </div>
      )}
      <label htmlFor={`${uid}-title`}>Plan title
        <input id={`${uid}-title`} type="text" maxLength={250} value={title} onChange={(e) => { setTitle(e.target.value); setMessage(null); }} aria-invalid={errors.title ? true : undefined} aria-describedby={errors.title ? `${uid}-title-problem` : undefined} />
        {errors.title && <span id={`${uid}-title-problem`} className="alv-tp__error">{errors.title}</span>}
      </label>
      {diagnoses.length === 0
        ? <p className="alv-clinical__meta" role="note">This patient has no current diagnosis. A treatment plan proposes procedures for a diagnosis, so record one in the Diagnoses tab first.</p>
        : <TreatmentPlanItemFields idBase={`${uid}-item`} diagnoses={diagnoses} procedures={procedures} value={item} onChange={(v) => { setItem(v); setMessage(null); }} errors={errors} prefix="items[0]." />}
      {rest.length > 0 && <ul className="alv-tp__error">{rest.map((m) => <li key={m}>{m}</li>)}</ul>}
      <p className="alv-clinical__meta">The fee is the practice's catalog fee today. It is an estimate for the practice, not an insurance estimate.</p>
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || diagnoses.length === 0}>Create treatment plan</button>
      </div>
    </form>
  );
}
