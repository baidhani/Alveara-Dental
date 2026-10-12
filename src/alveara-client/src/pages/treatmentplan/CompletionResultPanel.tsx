import { Link } from "react-router-dom";
import type { CompletionResult } from "../../services/completionApi";
import { money } from "./completionReviewRules";
import { when } from "./treatmentPlanRules";

const chartWords = (outcome: string): string =>
  outcome === "Updated" ? "chart finding marked Completed" : outcome === "AlreadyCompleted" ? "chart finding already Completed" : "no chart change";

/**
 * ALV-008-C01: shown only after the server confirmed the whole set. What each procedure became (when it was performed, what happened to the chart, what was charged) and where to see it next: the clinical
 * history, the odontogram and the account ledger.
 */
export function CompletionResultPanel({ result, patientId, onDone }: { result: CompletionResult; patientId: string; onDone: () => void }) {
  return (
    <section className="alv-dx__form" aria-label="Completion recorded">
      <h4>{result.replayed ? "Already completed: nothing new was recorded" : `${result.procedures.length} procedure${result.procedures.length === 1 ? "" : "s"} completed`}</h4>
      <ul aria-label="Completed procedures recorded">
        {result.procedures.map((p) => (
          <li key={p.id}>
            <strong>{p.procedureCode}</strong> {p.procedureDescription}: performed {when(p.performedAtUtc)}; {chartWords(p.chartOutcome)};{" "}
            {p.charge && p.charge.outcome !== "none" ? `charged ${money(p.charge.amount ?? 0)} (account entry ${p.charge.entryNumber})` : "no charge"}.
          </li>
        ))}
      </ul>
      <p className="alv-clinical__meta">
        See it in the <a href="#alv-ch-title" className="alv-workspace__link">clinical history</a>, on the <Link to={`/patients/${patientId}/odontogram`} className="alv-workspace__link">odontogram</Link> or in the <Link to={`/patients/${patientId}/billing`} className="alv-workspace__link">account ledger</Link>.
      </p>
      <button type="button" className="btn btn-outline-secondary" onClick={onDone}>Done</button>
    </section>
  );
}
