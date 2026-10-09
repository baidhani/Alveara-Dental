import { useId, useState } from "react";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import type { Diagnosis } from "../../services/diagnosisApi";
import { planFieldErrorsOf, renameTreatmentPlan, withdrawPlanItem, withdrawTreatmentPlan } from "../../services/treatmentPlansApi";
import type { PlanItem, PlanningProcedure, TreatmentPlan } from "../../services/treatmentPlansApi";
import { TreatmentPlanAddItemForm } from "./TreatmentPlanAddItemForm";
import { TreatmentPlanReasonForm } from "./TreatmentPlanReasonForm";
import { titleGap } from "./treatmentPlanRules";

export interface ChangeProps {
  /** Called with the plan as the server now holds it, after a change. */
  onChanged: (plan: TreatmentPlan, text: string) => void;
  /** A stale change: the panel shows the conflict banner and reloads. */
  onConflict: () => void;
}

/** The control on one active procedure: withdraw it, with a reason (a procedure is never edited or deleted). */
export function WithdrawItemControl({ plan, item, onChanged, onConflict }: ChangeProps & { plan: TreatmentPlan; item: PlanItem }) {
  const [open, setOpen] = useState(false);
  if (!open) return <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setOpen(true)}>Withdraw {item.procedureCode}</button>;
  return (
    <TreatmentPlanReasonForm title={`Withdraw ${item.procedureCode} from this plan`} hint="The procedure stays on the plan, marked Withdrawn with your reason, and its fee leaves the estimate total." question="Why is this procedure being withdrawn?"
      button="Withdraw procedure" past="withdrawn" act={(reason) => withdrawPlanItem(plan, item.id, reason)} onDone={(p) => { setOpen(false); onChanged(p, `${item.procedureCode} withdrawn from the plan.`); }} onConflict={onConflict} onCancel={() => setOpen(false)} />
  );
}

function RenameForm({ plan, onChanged, onConflict, onCancel }: ChangeProps & { plan: TreatmentPlan; onCancel: () => void }) {
  const [title, setTitle] = useState(plan.title);
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const id = useId();

  async function save() {
    const gap = titleGap(title);
    if (gap) return setNote(`Not renamed: ${gap}`);
    setBusy(true);
    setNote(null);
    try {
      onChanged(await renameTreatmentPlan(plan, title.trim()), "Treatment plan renamed.");
    } catch (err) {
      const fields = Object.values(planFieldErrorsOf(err));
      if (isConcurrencyConflict(err)) { setNote("Not renamed: someone else changed this treatment plan. Reload and try again."); onConflict(); }
      else if (fields.length > 0) setNote(`Not renamed: ${fields.join(" ")}`);
      else if (isNetworkFailure(err)) setNote("Not renamed: the connection dropped. Try again.");
      else setNote(`Not renamed: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label="Rename this treatment plan" onSubmit={(e) => { e.preventDefault(); void save(); }}>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      <label htmlFor={id}>New title
        <input id={id} type="text" maxLength={250} value={title} onChange={(e) => setTitle(e.target.value)} />
      </label>
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Save title</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}

type Mode = "add" | "rename" | "withdraw" | null;

interface Props extends ChangeProps { plan: TreatmentPlan; diagnoses: Diagnosis[]; procedures: PlanningProcedure[] }

/** STORY-015: what a person who may change plans can do with one that is still Proposed: add a procedure, rename it, or withdraw it with a reason. One form is open at a time. */
export function TreatmentPlanActions({ plan, diagnoses, procedures, onChanged, onConflict }: Props) {
  const [mode, setMode] = useState<Mode>(null);
  const done = (p: TreatmentPlan, text: string) => { setMode(null); onChanged(p, text); };
  const cancel = () => setMode(null);

  return (
    <div className="alv-tp__actions">
      {mode === null && (
        <div className="alv-clinical__row-actions">
          <button type="button" className="btn btn-outline-primary btn-sm" onClick={() => setMode("add")}>Add procedure</button>
          <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setMode("rename")}>Rename plan</button>
          <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setMode("withdraw")}>Withdraw plan</button>
        </div>
      )}
      {mode === "add" && <TreatmentPlanAddItemForm plan={plan} diagnoses={diagnoses} procedures={procedures} onDone={(p) => done(p, "Procedure added to the plan.")} onConflict={onConflict} onCancel={cancel} />}
      {mode === "rename" && <RenameForm plan={plan} onChanged={done} onConflict={onConflict} onCancel={cancel} />}
      {mode === "withdraw" && (
        <TreatmentPlanReasonForm title="Withdraw this treatment plan" hint="The plan is kept, marked Withdrawn with your reason, and can no longer be changed." question="Why is this plan being withdrawn?"
          button="Withdraw plan" past="withdrawn" act={(reason) => withdrawTreatmentPlan(plan, reason)} onDone={(p) => done(p, "Treatment plan withdrawn.")} onConflict={onConflict} onCancel={cancel} />
      )}
    </div>
  );
}
