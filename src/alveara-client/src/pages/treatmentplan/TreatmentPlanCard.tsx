import { useState } from "react";
import type { Diagnosis } from "../../services/diagnosisApi";
import type { PlanningProcedure, TreatmentPlan } from "../../services/treatmentPlansApi";
import { TreatmentPlanActions, WithdrawItemControl } from "./TreatmentPlanActions";
import type { ChangeProps } from "./TreatmentPlanActions";
import { TreatmentPlanEstimate } from "./TreatmentPlanEstimate";
import { TreatmentPlanPatientCopy } from "./TreatmentPlanPatientCopy";
import { TreatmentPlanPhaseGroups } from "./TreatmentPlanPhaseGroups";
import { TreatmentPlanRevisions } from "./TreatmentPlanRevisions";
import { TreatmentPlanStateControls } from "./TreatmentPlanStateControls";
import { answerWords, when } from "./treatmentPlanRules";

interface Props extends Partial<ChangeProps> {
  plan: TreatmentPlan;
  /** Whether the person may change plans. It only decides which controls are drawn (the server checks every call). */
  canWrite?: boolean;
  diagnoses?: Diagnosis[];
  procedures?: PlanningProcedure[];
  /** The patient's name for the patient copy, when the record has one. */
  patientName?: string;
}

/**
 * One treatment plan: its procedures grouped by phase, the rationale and the explanation for the patient, the estimate total and what that total is (the label from the server: the practice's catalog fees,
 * not an insurance estimate), which revision this is and the reference code old diagnosis notes can be tied to. A withdrawn plan is shown as Withdrawn with who withdrew it and why; nothing is ever deleted.
 */
export function TreatmentPlanCard({ plan, canWrite = false, diagnoses = [], procedures = [], patientName, onChanged, onConflict }: Props) {
  const [copy, setCopy] = useState(false);
  const withdrawn = plan.status === "Withdrawn";
  const change = canWrite && !withdrawn && onChanged && onConflict ? { onChanged, onConflict } : null;
  return (
    <li className={`alv-dx__item${withdrawn ? " alv-dx__item--withdrawn" : ""}`}>
      <h3 className="alv-dx__label">{plan.title} <span className="alv-dx__badge">{plan.status}</span></h3>
      <p className="alv-clinical__meta">
        Created{plan.createdByName ? ` by ${plan.createdByName}` : ""} on {when(plan.createdAtUtc)}
        {plan.updatedAtUtc ? ` · last changed${plan.updatedByName ? ` by ${plan.updatedByName}` : ""} on ${when(plan.updatedAtUtc)}` : ""}
      </p>
      <p className="alv-clinical__meta">Revision {plan.currentRevision}{plan.referenceCode ? ` · Reference code ${plan.referenceCode}` : ""}</p>
      {!withdrawn && <p className="alv-clinical__meta">Patient&apos;s answer: {answerWords(plan.acceptanceStatus)}</p>}
      {withdrawn && <p className="alv-clinical__meta">Withdrawn{plan.withdrawnByName ? ` by ${plan.withdrawnByName}` : ""}{plan.withdrawnAtUtc ? ` on ${when(plan.withdrawnAtUtc)}` : ""}: {plan.withdrawnReason}</p>}
      {plan.clinicalRationale && <p className="alv-tp__words"><strong>Clinical rationale:</strong> {plan.clinicalRationale}</p>}
      {plan.patientExplanation && <p className="alv-tp__words"><strong>For the patient:</strong> {plan.patientExplanation}</p>}
      <TreatmentPlanPhaseGroups plan={plan} control={change ? (i) => (i.isWithdrawn ? null : <><TreatmentPlanStateControls plan={plan} item={i} {...change} />{i.completion === "Completed" ? null : <WithdrawItemControl plan={plan} item={i} {...change} />}</>) : undefined} />
      <TreatmentPlanEstimate plan={plan} />
      <TreatmentPlanRevisions plan={plan} />
      {!withdrawn && (copy
        ? <TreatmentPlanPatientCopy plan={plan} patientName={patientName} onClose={() => setCopy(false)} />
        : <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setCopy(true)}>Show patient copy</button>)}
      {change && <TreatmentPlanActions plan={plan} diagnoses={diagnoses} procedures={procedures} {...change} />}
    </li>
  );
}
