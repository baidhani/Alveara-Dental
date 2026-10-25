import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { caseErrorMessage, isStaleCaseChange, updateLabCase, updateReferral } from "../../services/externalCasesApi";
import type { ExternalCase } from "../../services/externalCasesApi";
import { CaseFields } from "./CaseFields";
import { checkValues, valuesOf } from "./caseFormValues";
import type { CaseFormValues } from "./caseFormValues";
import "../followups/followups.css";

interface Props { c: ExternalCase; onSaved: (updated: ExternalCase) => void; onCancel: () => void; onStale: () => void }

/**
 * ALV-009-C02: edit a case that is not finished: what it is for, the follow-up date, the notes and (lab) the tooth, material and shade. The outside party, the visit and the linked treatment stay as they are; the whole set is
 * sent with the version the screen was showing, so a change by someone else meanwhile is explained and offered as a reload rather than overwritten.
 */
export function CaseEditForm({ c, onSaved, onCancel, onStale }: Props) {
  const [values, setValues] = useState<CaseFormValues>(valuesOf(c));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);
  const [busy, setBusy] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, []);
  const text = (s: string) => (s.trim() === "" ? null : s.trim());

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    const found = checkValues(c.kind, values, c.partyId);
    setErrors(found);
    if (Object.keys(found).length > 0) { setError("Fix the marked fields and save again."); return; }
    setBusy(true); setError(null);
    try {
      const result = c.kind === "Lab"
        ? await updateLabCase(c.id, { partyId: c.partyId, title: values.summary.trim(), dueOn: text(values.dueOn), toothKey: text(values.toothKey), treatmentPlanItemId: c.treatmentPlanItemId, material: text(values.material), shade: text(values.shade), notes: text(values.notes) }, c.rowVersion)
        : await updateReferral(c.id, { partyId: c.partyId, reason: values.summary.trim(), dueOn: text(values.dueOn), encounterId: c.encounterId, notes: text(values.notes) }, c.rowVersion);
      onSaved(result.case);
    } catch (err) {
      if (isStaleCaseChange(err)) setStale(true);
      setError(caseErrorMessage(err));
    } finally { setBusy(false); }
  }

  return (
    <form className="alv-fu-form" onSubmit={submit} aria-labelledby="alv-ec-edit-title" noValidate>
      <h2 id="alv-ec-edit-title" tabIndex={-1} ref={heading}>Edit: {c.summary}</h2>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
      {stale && <Button type="button" onClick={onStale}>Reload the case</Button>}
      <CaseFields idPrefix="alv-ec-edit" kind={c.kind} values={values} errors={errors} onChange={setValues} />
      <div className="alv-fu-actions">
        <Button type="submit" variant="primary" disabled={busy}>{busy ? "Saving…" : "Save changes"}</Button>
        <Button type="button" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  );
}
