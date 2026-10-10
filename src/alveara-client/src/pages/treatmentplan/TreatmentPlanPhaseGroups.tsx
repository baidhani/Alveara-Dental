import type { ReactNode } from "react";
import type { PlanItem, TreatmentPlan } from "../../services/treatmentPlansApi";
import { TreatmentPlanItemRow } from "./TreatmentPlanItemRow";
import { groupItems, phaseTitle, phaseWhen } from "./treatmentPlanLayout";

interface Props {
  plan: TreatmentPlan;
  /** What a person who may change the plan can do with one procedure (drawn under it). */
  control?: (item: PlanItem) => ReactNode;
}

/**
 * ALV-015-C01: the plan's procedures grouped by phase (a visit or stage of care), with the ones not yet placed last. A plan with no phases is the plain list it always was. Each group says what the phase
 * is called and when it is planned, and a phase with nothing in it says so, so an empty phase is never mistaken for a missing one.
 */
export function TreatmentPlanPhaseGroups({ plan, control }: Props) {
  const groups = groupItems(plan);
  const rows = (items: PlanItem[]) => items.map((i) => <TreatmentPlanItemRow key={i.id} item={i} control={control?.(i)} />);
  if (groups.length === 1 && groups[0].phase === null && plan.phases.length === 0) {
    return <ul className="alv-tp__items" aria-label={`Procedures in ${plan.title}`}>{rows(groups[0].items)}</ul>;
  }
  return (
    <div className="alv-tp__phases">
      {groups.map((g) => {
        const title = g.phase ? phaseTitle(g.phase) : "Not yet placed in a phase";
        const timing = g.phase ? phaseWhen(g.phase) : null;
        return (
          <section key={g.phase?.phaseNumber ?? "loose"} className="alv-tp__phase" aria-label={`${title} of ${plan.title}`}>
            <h4 className="alv-tp__phase-title">{title}</h4>
            {timing && <p className="alv-clinical__meta">{timing}</p>}
            {g.items.length === 0 ? <p className="alv-clinical__meta">No procedures in this phase yet.</p> : <ul className="alv-tp__items" aria-label={`Procedures in ${title}`}>{rows(g.items)}</ul>}
          </section>
        );
      })}
    </div>
  );
}
