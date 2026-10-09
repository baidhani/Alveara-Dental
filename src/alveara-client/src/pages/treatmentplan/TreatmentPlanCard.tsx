import { DEFAULT_NUMBERING, displayTooth } from "../odontogram/toothNumbering";
import type { Diagnosis } from "../../services/diagnosisApi";
import type { PlanItem, PlanningProcedure, TreatmentPlan } from "../../services/treatmentPlansApi";
import { TreatmentPlanActions, WithdrawItemControl } from "./TreatmentPlanActions";
import type { ChangeProps } from "./TreatmentPlanActions";
import { formatFee } from "./treatmentPlanRules";

const when = (iso: string) => new Date(iso).toLocaleString();

function siteOf(item: PlanItem) {
  if (!item.toothKey) return null;
  return `Tooth ${displayTooth(item.toothKey, DEFAULT_NUMBERING)}${item.surface ? `, surface ${item.surface}` : ""}`;
}

/** One proposed procedure, drawn from the plan response alone (so a role that may read plans but not the catalog sees it in full). A withdrawn item stays visible, says Withdrawn and why, and is left out of the total. */
function ItemRow({ item, control }: { item: PlanItem; control?: React.ReactNode }) {
  const site = siteOf(item);
  return (
    <li className={`alv-tp__item${item.isWithdrawn ? " alv-tp__item--withdrawn" : ""}`}>
      <p className="alv-tp__item-title">
        <strong>{item.procedureCode}</strong> {item.procedureDescription}
        {item.isWithdrawn && <> <span className="alv-dx__badge">Withdrawn</span></>}
      </p>
      <p className="alv-clinical__meta">
        For: {item.diagnosisLabel}{site ? ` · ${site}` : ""} · Fee {formatFee(item.fee)} (catalog version {item.procedureVersionNumber})
      </p>
      {item.isWithdrawn && <p className="alv-clinical__meta">Withdrawn{item.withdrawnByName ? ` by ${item.withdrawnByName}` : ""}{item.withdrawnAtUtc ? ` on ${when(item.withdrawnAtUtc)}` : ""}: {item.withdrawnReason}</p>}
      {control}
    </li>
  );
}

interface Props extends Partial<ChangeProps> {
  plan: TreatmentPlan;
  /** Whether the person may change plans. It only decides which controls are drawn (the server checks every call). */
  canWrite?: boolean;
  diagnoses?: Diagnosis[];
  procedures?: PlanningProcedure[];
}

/**
 * STORY-015: one treatment plan with its proposed procedures, the estimate total and what that total is (the label from the server: the practice's catalog fees, not an insurance estimate). A withdrawn plan
 * is shown as Withdrawn with who withdrew it and why; nothing is ever deleted.
 */
export function TreatmentPlanCard({ plan, canWrite = false, diagnoses = [], procedures = [], onChanged, onConflict }: Props) {
  const withdrawn = plan.status === "Withdrawn";
  const change = canWrite && !withdrawn && onChanged && onConflict ? { onChanged, onConflict } : null;
  return (
    <li className={`alv-dx__item${withdrawn ? " alv-dx__item--withdrawn" : ""}`}>
      <h3 className="alv-dx__label">{plan.title} <span className="alv-dx__badge">{plan.status}</span></h3>
      <p className="alv-clinical__meta">
        Created{plan.createdByName ? ` by ${plan.createdByName}` : ""} on {when(plan.createdAtUtc)}
        {plan.updatedAtUtc ? ` · last changed${plan.updatedByName ? ` by ${plan.updatedByName}` : ""} on ${when(plan.updatedAtUtc)}` : ""}
      </p>
      {withdrawn && <p className="alv-clinical__meta">Withdrawn{plan.withdrawnByName ? ` by ${plan.withdrawnByName}` : ""}{plan.withdrawnAtUtc ? ` on ${when(plan.withdrawnAtUtc)}` : ""}: {plan.withdrawnReason}</p>}
      <ul className="alv-tp__items" aria-label={`Procedures in ${plan.title}`}>{plan.items.map((i) => <ItemRow key={i.id} item={i} control={change && !i.isWithdrawn ? <WithdrawItemControl plan={plan} item={i} {...change} /> : undefined} />)}</ul>
      <p className="alv-tp__total">
        <strong>Estimate total: {formatFee(plan.estimateTotal)}</strong> for {plan.activeItemCount} {plan.activeItemCount === 1 ? "procedure" : "procedures"}
      </p>
      <p className="alv-clinical__meta">{plan.estimateLabel}</p>
      {change && <TreatmentPlanActions plan={plan} diagnoses={diagnoses} procedures={procedures} {...change} />}
    </li>
  );
}
