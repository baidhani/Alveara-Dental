import { useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { CONDITIONS, CONDITION_LABELS, FINDING_STATES, SURFACE_CONDITIONS } from "../../services/odontogramApi";
import type { RecordFindingInput } from "../../services/odontogramApi";
import type { SaveResult } from "../safety/SafetyForms";
import { STATE_MEANING } from "./odontogramText";
import { SURFACE_NAMES, surfacesFor } from "./toothNumbering";

interface Props {
  toothKey: string;
  /** The surface selected in the tooth's details, offered as the starting choice for a surface condition (never forced). */
  defaultSurface: string | null;
  busy: boolean;
  onSubmit: (input: RecordFindingInput) => Promise<SaveResult>;
  onCancel: () => void;
}

/**
 * STORY-006: records one finding on the selected tooth. The condition and the state are CHOSEN, never pre-filled with a guess, so a finding is always saved in the state the
 * clinician picked. A surface is asked for only when the condition is about one surface, and only the surfaces that exist on this tooth are offered; a whole-tooth condition sends
 * none. The same rules are checked again by the server, whose refusals are shown against the field they name. What was typed stays until the server has accepted it.
 */
export function RecordFindingForm({ toothKey, defaultSurface, busy, onSubmit, onCancel }: Props) {
  const surfaces = surfacesFor(toothKey);
  const [condition, setCondition] = useState("");
  const [surface, setSurface] = useState(defaultSurface && surfaces.includes(defaultSurface) ? defaultSurface : "");
  const [state, setState] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const conditionId = useId();
  const surfaceId = useId();
  const stateId = useId();
  const needsSurface = SURFACE_CONDITIONS.has(condition);
  useUnsavedChangesWarning(condition !== "" || state !== "");

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    const found: Record<string, string> = {};
    if (condition === "") found.condition = "Choose the condition.";
    if (needsSurface && surface === "") found.surface = "Choose the surface this applies to.";
    if (state === "") found.state = "Choose where it is in its lifecycle.";
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    const refused = await onSubmit({ toothKey, surface: needsSurface ? surface : null, condition, state });
    if (refused) setErrors(refused);
  }

  return (
    <form className="alv-safety__form" onSubmit={submit} noValidate aria-label="Record a finding">
      <div className="alv-form-field">
        <label htmlFor={conditionId} className="alv-form-field__label">Condition</label>
        <select id={conditionId} className="alv-form-field__input" value={condition} aria-invalid={errors.condition ? true : undefined} onChange={(e) => setCondition(e.target.value)} autoFocus>
          <option value="">Choose…</option>
          {CONDITIONS.map((c) => <option key={c} value={c}>{CONDITION_LABELS[c]}</option>)}
        </select>
        {errors.condition && <p className="alv-form-field__error" role="alert">{errors.condition}</p>}
      </div>
      {needsSurface && (
        <div className="alv-form-field">
          <label htmlFor={surfaceId} className="alv-form-field__label">Surface</label>
          <select id={surfaceId} className="alv-form-field__input" value={surface} aria-invalid={errors.surface ? true : undefined} onChange={(e) => setSurface(e.target.value)}>
            <option value="">Choose…</option>
            {surfaces.map((s) => <option key={s} value={s}>{SURFACE_NAMES[s]}</option>)}
          </select>
          {errors.surface && <p className="alv-form-field__error" role="alert">{errors.surface}</p>}
        </div>
      )}
      {condition !== "" && !needsSurface && <p className="alv-form-field__hint">{CONDITION_LABELS[condition]} applies to the whole tooth, so no surface is chosen.</p>}
      <div className="alv-form-field">
        <label htmlFor={stateId} className="alv-form-field__label">State</label>
        <select id={stateId} className="alv-form-field__input" value={state} aria-invalid={errors.state ? true : undefined} onChange={(e) => setState(e.target.value)}>
          <option value="">Choose…</option>
          {FINDING_STATES.map((s) => <option key={s} value={s}>{s} - {STATE_MEANING[s]}</option>)}
        </select>
        {errors.state && <p className="alv-form-field__error" role="alert">{errors.state}</p>}
      </div>
      <div className="alv-clinical__row-actions">
        <Button type="submit" variant="primary" disabled={busy}>Record finding</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}
