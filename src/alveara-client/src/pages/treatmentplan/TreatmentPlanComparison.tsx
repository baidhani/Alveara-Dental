import type { FieldChange, RevisionComparison, RevisionItem } from "../../services/treatmentPlansApi";
import { phaseTitle } from "./treatmentPlanLayout";
import { formatDelta, formatFee } from "./treatmentPlanRules";

const shown = (v: string | null) => (v === null || v === "" ? "(none)" : v);

function Words({ label, change }: { label: string; change: FieldChange | null }) {
  if (!change) return null;
  return <li><strong>{label} changed.</strong> Before: {shown(change.from)} After: {shown(change.to)}</li>;
}

function Items({ label, items }: { label: string; items: RevisionItem[] }) {
  if (items.length === 0) return null;
  return <li><strong>{label}:</strong> {items.map((i) => `${i.procedureCode} ${i.procedureDescription} (${formatFee(i.fee)})`).join("; ")}</li>;
}

/**
 * ALV-015-C01: what changed between two saved revisions of a plan, named in words: the title, the clinical rationale and the patient explanation, the phases added, removed or changed, the procedures added,
 * withdrawn or moved, and the practice fee estimate of each revision with the change between them. Both sides are read from what was saved, so a later catalog price never appears here.
 */
export function TreatmentPlanComparison({ comparison: c }: { comparison: RevisionComparison }) {
  const nothing = !c.title && !c.clinicalRationale && !c.patientExplanation && c.phasesAdded.length + c.phasesRemoved.length + c.phasesChanged.length === 0
    && c.itemsAdded.length + c.itemsWithdrawn.length + c.itemsMoved.length === 0;
  return (
    <section className="alv-tp__revision" aria-label={`Changes from revision ${c.from.revisionNumber} to revision ${c.to.revisionNumber}`}>
      <h4 className="alv-tp__phase-title">Changes from revision {c.from.revisionNumber} to revision {c.to.revisionNumber}</h4>
      {nothing ? <p className="alv-clinical__meta">No differences in the title, words, phases or procedures.</p> : (
        <ul className="alv-tp__changes">
          <Words label="Title" change={c.title} />
          <Words label="Clinical rationale" change={c.clinicalRationale} />
          <Words label="Explanation for the patient" change={c.patientExplanation} />
          {c.phasesAdded.length > 0 && <li><strong>Phases added:</strong> {c.phasesAdded.map(phaseTitle).join("; ")}</li>}
          {c.phasesRemoved.length > 0 && <li><strong>Phases removed:</strong> {c.phasesRemoved.map(phaseTitle).join("; ")}</li>}
          {c.phasesChanged.map((p) => <li key={p.phaseNumber}><strong>Phase {p.phaseNumber} changed.</strong> Before: {phaseTitle(p.from)}{p.from.plannedVisit ? ` (${p.from.plannedVisit})` : ""} After: {phaseTitle(p.to)}{p.to.plannedVisit ? ` (${p.to.plannedVisit})` : ""}</li>)}
          <Items label="Procedures added" items={c.itemsAdded} />
          <Items label="Procedures withdrawn" items={c.itemsWithdrawn} />
          {c.itemsMoved.map((m) => <li key={m.item.itemId}><strong>{m.item.procedureCode} moved</strong> from {m.fromPhase ? `phase ${m.fromPhase}` : "not placed"} to {m.toPhase ? `phase ${m.toPhase}` : "not placed"}.</li>)}
        </ul>
      )}
      <p className="alv-tp__total"><strong>Estimate: {formatFee(c.estimateFrom)} then, {formatFee(c.estimateTo)} now ({formatDelta(c.estimateDelta)})</strong></p>
      <p className="alv-clinical__meta">{c.estimateLabel}</p>
    </section>
  );
}
