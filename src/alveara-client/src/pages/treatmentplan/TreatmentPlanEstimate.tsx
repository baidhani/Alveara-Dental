import type { TreatmentPlan } from "../../services/treatmentPlansApi";
import { formatFee } from "./treatmentPlanRules";

/**
 * ALV-015-C01: the fee estimate of a plan, with where the figure comes from and what it is not. It is the practice's own catalog fee for each procedure as it was when the procedure was proposed, added up;
 * the words come from the server (so every screen and print says the same thing) and nothing here implies insurance has been consulted or that this is what a patient will be charged.
 */
export function TreatmentPlanEstimate({ plan }: { plan: TreatmentPlan }) {
  return (
    <div className="alv-tp__estimate">
      <p className="alv-tp__total">
        <strong>Estimate total: {formatFee(plan.estimateTotal)}</strong> for {plan.activeItemCount} {plan.activeItemCount === 1 ? "procedure" : "procedures"}
      </p>
      {plan.acceptedEstimateTotal > 0 && <p className="alv-clinical__meta">Of that, accepted so far: {formatFee(plan.acceptedEstimateTotal)}</p>}
      <p className="alv-clinical__meta">{plan.estimateLabel}</p>
      <p className="alv-clinical__meta">Source: the practice&apos;s fee schedule, as it stood when each procedure was proposed. No insurance benefits, discounts or payment plans are applied, and the total changes if the plan is revised.</p>
    </div>
  );
}
