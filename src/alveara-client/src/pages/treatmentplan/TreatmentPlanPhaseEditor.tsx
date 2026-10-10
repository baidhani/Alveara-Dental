import { PHASES_MAX, PHASE_NAME_MAX, VISIT_MAX, nextPhaseNumber } from "./treatmentPlanLayout";
import type { PhaseDraft } from "./treatmentPlanLayout";

interface Props {
  phases: PhaseDraft[];
  onChange: (phases: PhaseDraft[]) => void;
  /** Removing a phase is the parent's job because it also puts that phase's procedures back to not yet placed. */
  onRemove: (number: number) => void;
  /** The server's and the form's messages, keyed by field name (`phases[0].name`). */
  errors: Record<string, string>;
  idBase: string;
}

/** ALV-015-C01: the rows that lay out a plan's phases: a name, a note about the planned visit and an optional target date. A phase's number is fixed once it exists, so procedures keep following it across revisions. */
export function TreatmentPlanPhaseEditor({ phases, onChange, onRemove, errors, idBase }: Props) {
  const set = (i: number, patch: Partial<PhaseDraft>) => onChange(phases.map((p, j) => (j === i ? { ...p, ...patch } : p)));
  const next = nextPhaseNumber(phases);
  const error = (key: string) => errors[key] && <span id={`${idBase}-${key}-problem`} className="alv-tp__error">{errors[key]}</span>;

  return (
    <div className="alv-tp__phase-editor">
      {phases.length === 0 && <p className="alv-clinical__meta">No phases. Procedures are listed together; add a phase to group them by visit or stage of care.</p>}
      {phases.map((p, i) => (
        <div key={p.number} className="alv-tp__phase-row" role="group" aria-label={`Phase ${p.number}`}>
          <label htmlFor={`${idBase}-name-${p.number}`}>Phase {p.number} name
            <input id={`${idBase}-name-${p.number}`} type="text" maxLength={PHASE_NAME_MAX + 20} value={p.name} onChange={(e) => set(i, { name: e.target.value })}
              aria-invalid={errors[`phases[${i}].name`] ? true : undefined} aria-describedby={errors[`phases[${i}].name`] ? `${idBase}-phases[${i}].name-problem` : undefined} />
            {error(`phases[${i}].name`)}
          </label>
          <label htmlFor={`${idBase}-visit-${p.number}`}>Phase {p.number} planned visit (optional)
            <input id={`${idBase}-visit-${p.number}`} type="text" maxLength={VISIT_MAX + 20} value={p.plannedVisit} onChange={(e) => set(i, { plannedVisit: e.target.value })}
              aria-invalid={errors[`phases[${i}].plannedVisit`] ? true : undefined} />
            {error(`phases[${i}].plannedVisit`)}
          </label>
          <label htmlFor={`${idBase}-date-${p.number}`}>Phase {p.number} target date (optional)
            <input id={`${idBase}-date-${p.number}`} type="date" value={p.targetDate} onChange={(e) => set(i, { targetDate: e.target.value })} aria-invalid={errors[`phases[${i}].targetDate`] ? true : undefined} />
            {error(`phases[${i}].targetDate`)}
          </label>
          {error(`phases[${i}].number`)}
          <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => onRemove(p.number)}>Remove phase {p.number}</button>
        </div>
      ))}
      {errors.phases && <span className="alv-tp__error" role="alert">{errors.phases}</span>}
      <button type="button" className="btn btn-outline-primary btn-sm" disabled={next === null} onClick={() => next !== null && onChange([...phases, { number: next, name: "", plannedVisit: "", targetDate: "" }])}>
        Add a phase{next === null ? ` (at most ${PHASES_MAX})` : ""}
      </button>
    </div>
  );
}
