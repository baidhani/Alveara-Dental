import { DEFAULT_NUMBERING, displayTooth } from "../odontogram/toothNumbering";
import type { RevisionDetail, RevisionItem } from "../../services/treatmentPlansApi";
import { groupRevisionItems, phaseTitle, phaseWhen } from "./treatmentPlanLayout";
import { changeWords, formatFee, when } from "./treatmentPlanRules";

function Item({ item }: { item: RevisionItem }) {
  const site = item.toothKey ? `Tooth ${displayTooth(item.toothKey, DEFAULT_NUMBERING)}${item.surface ? `, surface ${item.surface}` : ""}` : null;
  return (
    <li className={`alv-tp__item${item.isWithdrawn ? " alv-tp__item--withdrawn" : ""}`}>
      <strong>{item.procedureCode}</strong> {item.procedureDescription}{item.isWithdrawn && <> <span className="alv-dx__badge">Withdrawn by then</span></>}
      <span className="alv-clinical__meta"> · For: {item.diagnosisLabel}{site ? ` · ${site}` : ""} · Fee {formatFee(item.fee)}</span>
    </li>
  );
}

/**
 * ALV-015-C01: one saved version of a plan, read-only and exactly as it was saved: its title, words, phases and procedures, and what the practice fee estimate came to then. An earlier version is never
 * edited, so what this shows is what a colleague or a patient could have seen at the time.
 */
export function TreatmentPlanRevisionView({ revision }: { revision: RevisionDetail }) {
  const s = revision.summary;
  return (
    <section className="alv-tp__revision" aria-label={`Revision ${s.revisionNumber} as saved`}>
      <h4 className="alv-tp__phase-title">Revision {s.revisionNumber} as saved: {s.title}</h4>
      <p className="alv-clinical__meta">{changeWords(s.changeType)}{s.actorName ? ` by ${s.actorName}` : ""} on {when(s.createdAtUtc)}{s.reason ? `. Reason: ${s.reason}` : ""}</p>
      {revision.clinicalRationale && <p className="alv-tp__words"><strong>Clinical rationale:</strong> {revision.clinicalRationale}</p>}
      {revision.patientExplanation && <p className="alv-tp__words"><strong>For the patient:</strong> {revision.patientExplanation}</p>}
      {groupRevisionItems(revision).map((g) => (
        <div key={g.phase?.phaseNumber ?? "loose"} className="alv-tp__phase">
          {(g.phase || revision.phases.length > 0) && <p className="alv-tp__phase-title"><strong>{g.phase ? phaseTitle(g.phase) : "Not yet placed in a phase"}</strong>{g.phase && phaseWhen(g.phase) ? ` · ${phaseWhen(g.phase)}` : ""}</p>}
          {g.items.length === 0 ? <p className="alv-clinical__meta">No procedures in this phase.</p> : <ul className="alv-tp__items">{g.items.map((i) => <Item key={i.itemId} item={i} />)}</ul>}
        </div>
      ))}
      <p className="alv-clinical__meta">Estimate then: {formatFee(s.estimateTotal)} for {s.procedureCount} {s.procedureCount === 1 ? "procedure" : "procedures"}. {revision.estimateLabel}</p>
    </section>
  );
}
