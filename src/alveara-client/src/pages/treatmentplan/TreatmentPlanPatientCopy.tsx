import { DEFAULT_NUMBERING, displayTooth } from "../odontogram/toothNumbering";
import type { PlanItem, TreatmentPlan } from "../../services/treatmentPlansApi";
import { groupItems, phaseTitle, phaseWhen } from "./treatmentPlanLayout";
import { dayOf, formatFee } from "./treatmentPlanRules";

interface Props {
  plan: TreatmentPlan;
  /** The patient's name as the record has it; left out of the page when the record has none. */
  patientName?: string;
  onClose: () => void;
}

const siteWords = (i: PlanItem) => (i.toothKey ? `Tooth ${displayTooth(i.toothKey, DEFAULT_NUMBERING)}${i.surface ? `, surface ${i.surface}` : ""}` : null);

function answerWords(i: PlanItem): string {
  const parts = [i.acceptance === "Accepted" ? "Your answer: accepted" : i.acceptance === "Declined" ? "Your answer: declined" : "Your answer: not yet given"];
  if (i.scheduling === "Scheduled") parts.push(i.plannedDate ? `planned for ${dayOf(i.plannedDate)}` : "planned");
  if (i.completion === "Completed") parts.push("done");
  return parts.join(" · ");
}

function Row({ item }: { item: PlanItem }) {
  const site = siteWords(item);
  return (
    <li className="alv-tp__copy-item">
      <p className="alv-tp__copy-line"><strong>{item.procedureDescription}</strong>{site ? ` (${site})` : ""}</p>
      <p className="alv-tp__copy-line">For: {item.diagnosisLabel}</p>
      <p className="alv-tp__copy-line">Estimated fee: {formatFee(item.fee)} · {answerWords(item)}</p>
    </li>
  );
}

/**
 * ALV-015-C01: the plan as a patient can take it home: the title, the explanation in the dentist's own plain words, the procedures in stages (phases) with what each is for and its estimated fee, and the practice's
 * estimate with what it is not. It has a Print button and prints as a plain page (everything else on the screen is hidden when printing). It deliberately leaves out what is for staff: the clinical rationale, who entered
 * what, the revision history and anything withdrawn. Nothing here claims insurance has been consulted or says what the patient will pay.
 */
export function TreatmentPlanPatientCopy({ plan, patientName, onClose }: Props) {
  const live = { ...plan, items: plan.items.filter((i) => !i.isWithdrawn) };
  const groups = groupItems(live);
  const today = new Date().toLocaleDateString();
  return (
    <section className="alv-tp__copy" aria-label={`Patient copy of ${plan.title}`}>
      <div className="alv-tp__copy-actions alv-tp__no-print">
        <button type="button" className="btn btn-primary btn-sm" onClick={() => window.print()}>Print this page</button>
        <button type="button" className="btn btn-outline-secondary btn-sm" onClick={onClose}>Close patient copy</button>
      </div>
      <h3 className="alv-tp__copy-title">Your treatment plan: {plan.title}</h3>
      <p className="alv-tp__copy-line">{patientName ? `Prepared for ${patientName} on ${today}.` : `Prepared on ${today}.`}</p>
      <p className="alv-tp__copy-line">{plan.patientExplanation ?? "Your dentist has proposed the care below. Please ask if anything is unclear."}</p>

      {groups.map((g) => (
        <div key={g.phase?.phaseNumber ?? "all"} className="alv-tp__copy-phase">
          {g.phase ? <h4 className="alv-tp__copy-phase-title">{phaseTitle(g.phase)}</h4> : plan.phases.length > 0 ? <h4 className="alv-tp__copy-phase-title">To be planned</h4> : null}
          {g.phase && phaseWhen(g.phase) && <p className="alv-tp__copy-line">{phaseWhen(g.phase)}</p>}
          {g.items.length === 0 ? <p className="alv-tp__copy-line">Nothing in this stage yet.</p> : <ul className="alv-tp__copy-items">{g.items.map((i) => <Row key={i.id} item={i} />)}</ul>}
        </div>
      ))}

      <p className="alv-tp__copy-total"><strong>Estimated total: {formatFee(live.items.reduce((sum, i) => sum + i.fee, 0))}</strong></p>
      {plan.acceptedEstimateTotal > 0 && <p className="alv-tp__copy-line">Of that, the part you have accepted so far: {formatFee(plan.acceptedEstimateTotal)}</p>}
      <p className="alv-tp__copy-line">{plan.estimateLabel}</p>
      <p className="alv-tp__copy-line">This is the practice&apos;s own estimate from its fee schedule. It does not include any insurance benefits, and it is not a quote of what you will be asked to pay. Ask the practice if you have questions.</p>
      <p className="alv-tp__copy-foot">Plan version {plan.currentRevision}{plan.referenceCode ? ` · Reference ${plan.referenceCode}` : ""}</p>
    </section>
  );
}
