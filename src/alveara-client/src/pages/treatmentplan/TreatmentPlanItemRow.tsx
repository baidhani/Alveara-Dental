import type { ReactNode } from "react";
import { DEFAULT_NUMBERING, displayTooth } from "../odontogram/toothNumbering";
import type { PlanItem } from "../../services/treatmentPlansApi";
import { TreatmentPlanItemStateLine } from "./TreatmentPlanItemStateLine";
import { formatFee, when } from "./treatmentPlanRules";

function siteOf(item: PlanItem) {
  if (!item.toothKey) return null;
  return `Tooth ${displayTooth(item.toothKey, DEFAULT_NUMBERING)}${item.surface ? `, surface ${item.surface}` : ""}`;
}

/**
 * One proposed procedure, drawn from the plan response alone (so a role that may read plans but not the catalog sees it in full). A withdrawn procedure stays visible, says Withdrawn and why, and is left
 * out of the total. <paramref name="control"/> is where the screen puts what a person who may change the plan can do with it.
 */
export function TreatmentPlanItemRow({ item, control }: { item: PlanItem; control?: ReactNode }) {
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
      {!item.isWithdrawn && <TreatmentPlanItemStateLine item={item} />}
      {control}
    </li>
  );
}
