import { ALL_KEYS, DEFAULT_NUMBERING, displayTooth, toothName } from "../odontogram/toothNumbering";
import type { Diagnosis } from "../../services/diagnosisApi";
import type { PlanningProcedure } from "../../services/treatmentPlansApi";
import { fieldMessage, formatFee, siteNeedOf } from "./treatmentPlanRules";
import type { ItemDraft } from "./treatmentPlanRules";

const SURFACES: readonly [string, string][] = [["M", "Mesial"], ["D", "Distal"], ["O", "Occlusal"], ["I", "Incisal"], ["B", "Buccal"], ["F", "Facial"], ["L", "Lingual"]];

interface Props {
  idBase: string;
  diagnoses: Diagnosis[];
  procedures: PlanningProcedure[];
  value: ItemDraft;
  onChange: (next: ItemDraft) => void;
  /** The server's field messages; `prefix` is "items[0]." when a plan is created and "" when one procedure is added. */
  errors: Record<string, string>;
  prefix?: string;
}

/**
 * STORY-015: the fields that name one proposed procedure: the patient's current diagnosis it is for, the active catalog procedure (shown with its fee, which is copied when the plan is saved) and, only
 * where the procedure is done on a tooth, the tooth and surface. The server judges every entry; its message is shown beside the field it is about.
 */
export function TreatmentPlanItemFields({ idBase, diagnoses, procedures, value, onChange, errors, prefix = "" }: Props) {
  const need = value.procedure ? siteNeedOf(value.procedure.scope) : "none";
  const field = (name: string) => {
    const message = fieldMessage(errors, name, prefix);
    return { id: `${idBase}-${name}`, message, invalid: message ? true : undefined, describedBy: message ? `${idBase}-${name}-problem` : undefined };
  };
  const diagnosis = field("diagnosisId"), procedure = field("procedureId"), tooth = field("toothKey"), surface = field("surface");
  const problem = (f: ReturnType<typeof field>) => f.message && <span id={`${f.id}-problem`} className="alv-tp__error">{f.message}</span>;

  return (
    <>
      <label htmlFor={diagnosis.id}>Diagnosis this procedure is for
        <select id={diagnosis.id} value={value.diagnosisId} onChange={(e) => onChange({ ...value, diagnosisId: e.target.value })} aria-invalid={diagnosis.invalid} aria-describedby={diagnosis.describedBy}>
          <option value="">Choose a diagnosis</option>
          {diagnoses.map((d) => <option key={d.id} value={d.id}>{d.label}{d.toothKey ? ` (tooth ${displayTooth(d.toothKey, DEFAULT_NUMBERING)})` : ""}</option>)}
        </select>
        {problem(diagnosis)}
      </label>
      <label htmlFor={procedure.id}>Procedure
        <select id={procedure.id} value={value.procedure?.procedureId ?? ""} aria-invalid={procedure.invalid} aria-describedby={procedure.describedBy}
          onChange={(e) => onChange({ ...value, procedure: procedures.find((p) => p.procedureId === e.target.value) ?? null, toothKey: "", surface: "" })}>
          <option value="">Choose a procedure</option>
          {procedures.map((p) => <option key={p.procedureId} value={p.procedureId}>{p.code} - {p.description} ({formatFee(p.fee)})</option>)}
        </select>
        {problem(procedure)}
      </label>
      {need !== "none" && (
        <label htmlFor={tooth.id}>Tooth
          <select id={tooth.id} value={value.toothKey} onChange={(e) => onChange({ ...value, toothKey: e.target.value })} aria-invalid={tooth.invalid} aria-describedby={tooth.describedBy}>
            <option value="">Choose a tooth</option>
            {ALL_KEYS.map((k) => <option key={k} value={k}>{displayTooth(k, DEFAULT_NUMBERING)} - {toothName(k)}</option>)}
          </select>
          {problem(tooth)}
        </label>
      )}
      {need === "toothAndSurface" && (
        <label htmlFor={surface.id}>Surface
          <select id={surface.id} value={value.surface} onChange={(e) => onChange({ ...value, surface: e.target.value })} aria-invalid={surface.invalid} aria-describedby={surface.describedBy}>
            <option value="">Choose a surface</option>
            {SURFACES.map(([code, name]) => <option key={code} value={code}>{name} ({code})</option>)}
          </select>
          {problem(surface)}
        </label>
      )}
    </>
  );
}
