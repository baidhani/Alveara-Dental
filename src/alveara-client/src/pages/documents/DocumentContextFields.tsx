import { useId } from "react";
import type { DocumentContext } from "../../services/documentsApi";
import type { EncounterSummary } from "../../services/clinicalApi";
import { REGIONS, REGION_LABELS } from "../diagnosis/diagnosisStructureRules";
import { encounterLabel } from "./documentText";
import { TEETH, toothOptionLabel } from "./documentContext";
import type { ContextErrors } from "./documentContext";

/**
 * ALV-010-C01: what a document is about: an encounter of the patient and one tooth or one oral region (the same lists a diagnosis uses). A document is about a tooth or a region, not both: choosing one clears the other.
 * Each box is optional and a refusal from the server is shown beside the box it is about.
 */
export function DocumentContextFields({ value, onChange, encounters, errors, disabled = false }: {
  value: DocumentContext; onChange: (next: DocumentContext) => void; encounters: EncounterSummary[]; errors: ContextErrors; disabled?: boolean;
}) {
  const id = useId();
  const field = (name: keyof ContextErrors) => ({ "aria-invalid": errors[name] ? true : undefined, "aria-describedby": errors[name] ? `${id}-${name}-error` : undefined });
  const message = (name: keyof ContextErrors) => errors[name] && <p id={`${id}-${name}-error`} className="alv-form-field__error" role="alert">{errors[name]}</p>;
  const ordered = [...encounters].sort((a, b) => b.encounterAtUtc.localeCompare(a.encounterAtUtc));

  return (
    <>
      <div className="alv-form-field">
        <label htmlFor={`${id}-encounter`} className="alv-form-field__label">Encounter (optional)</label>
        <select id={`${id}-encounter`} className="alv-form-field__input" value={value.encounterId} disabled={disabled} onChange={(e) => onChange({ ...value, encounterId: e.target.value })} {...field("encounter")}>
          <option value="">Not about one encounter</option>
          {ordered.map((e) => <option key={e.id} value={e.id}>{encounterLabel(e.encounterAtUtc)}</option>)}
        </select>
        {message("encounter")}
      </div>
      <div className="alv-form-field">
        <label htmlFor={`${id}-tooth`} className="alv-form-field__label">Tooth (optional)</label>
        <select id={`${id}-tooth`} className="alv-form-field__input" value={value.toothKey} disabled={disabled} onChange={(e) => onChange({ ...value, toothKey: e.target.value, regionKey: e.target.value ? "" : value.regionKey })} {...field("toothKey")}>
          <option value="">Not about one tooth</option>
          {TEETH.map((k) => <option key={k} value={k}>{toothOptionLabel(k)}</option>)}
        </select>
        {message("toothKey")}
      </div>
      <div className="alv-form-field">
        <label htmlFor={`${id}-region`} className="alv-form-field__label">Oral region (optional)</label>
        <select id={`${id}-region`} className="alv-form-field__input" value={value.regionKey} disabled={disabled} onChange={(e) => onChange({ ...value, regionKey: e.target.value, toothKey: e.target.value ? "" : value.toothKey })} {...field("regionKey")}>
          <option value="">Not about a region</option>
          {REGIONS.map((r) => <option key={r} value={r}>{REGION_LABELS[r]}</option>)}
        </select>
        {message("regionKey")}
      </div>
    </>
  );
}
