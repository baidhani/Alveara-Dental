import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { caseErrorMessage, createLabCase, createReferral, listParties, newCaseKey } from "../../services/externalCasesApi";
import type { CaseKind, ExternalCase, ExternalParty } from "../../services/externalCasesApi";
import { CaseFields } from "./CaseFields";
import { checkValues, emptyValues } from "./caseFormValues";
import type { CaseFormValues } from "./caseFormValues";
import { kindLabel, partyKindOf } from "./externalCaseFormat";
import "../followups/followups.css";

interface Props { patientId: string; kind: CaseKind; onCreated: (made: ExternalCase) => void; onCancel: () => void }

/**
 * ALV-009-C02: start a referral or a lab case for a patient. The outside party is chosen from the active ones of the right kind (if there are none, the form says who can add one); every field is checked against its limit
 * before anything is sent, and the attempt carries an Idempotency-Key that only changes when the form does, so a retry or a double click makes one case.
 */
export function NewCaseForm({ patientId, kind, onCreated, onCancel }: Props) {
  const [parties, setParties] = useState<ExternalParty[] | null>(null);
  const [partyId, setPartyId] = useState("");
  const [values, setValues] = useState<CaseFormValues>(emptyValues());
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const key = useRef(newCaseKey());
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, []);

  useEffect(() => {
    const controller = new AbortController();
    listParties(partyKindOf(kind), false, controller.signal).then(setParties).catch(() => { if (!controller.signal.aborted) setParties([]); });
    return () => controller.abort();
  }, [kind]);

  const change = (next: CaseFormValues) => { key.current = newCaseKey(); setValues(next); };

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    const found = checkValues(kind, values, partyId);
    setErrors(found);
    if (Object.keys(found).length > 0) { setFormError("Fix the marked fields and save again."); return; }
    setBusy(true); setFormError(null);
    const text = (s: string) => (s.trim() === "" ? null : s.trim());
    try {
      const result = kind === "Lab"
        ? await createLabCase(patientId, { partyId, title: values.summary.trim(), dueOn: text(values.dueOn), toothKey: text(values.toothKey), treatmentPlanItemId: null, material: text(values.material), shade: text(values.shade), notes: text(values.notes) }, key.current)
        : await createReferral(patientId, { partyId, reason: values.summary.trim(), dueOn: text(values.dueOn), encounterId: null, notes: text(values.notes) }, key.current);
      onCreated(result.case);
    } catch (err) { setFormError(caseErrorMessage(err)); } finally { setBusy(false); }
  }

  return (
    <form className="alv-fu-form" onSubmit={submit} aria-labelledby="alv-ec-new-title" noValidate>
      <h2 id="alv-ec-new-title" tabIndex={-1} ref={heading}>New {kindLabel(kind).toLowerCase()}</h2>
      {formError && <p role="alert" className="alv-fu-error">{formError}</p>}
      <div className="alv-form-field">
        <label htmlFor="alv-ec-new-party" className="alv-form-field__label">{kind === "Lab" ? "Laboratory *" : "Refer to *"}</label>
        <select id="alv-ec-new-party" value={partyId} onChange={(e) => { key.current = newCaseKey(); setPartyId(e.target.value); }} aria-invalid={!!errors.party} aria-describedby="alv-ec-new-party-msg" disabled={parties === null}>
          <option value="">{parties === null ? "Loading…" : "Choose…"}</option>
          {(parties ?? []).map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
        </select>
        <p id="alv-ec-new-party-msg" className={errors.party ? "alv-fu-error" : "alv-fu-item__meta"}>
          {errors.party ?? (parties !== null && parties.length === 0 ? `There is no active ${kind === "Lab" ? "laboratory" : "referral provider"} yet. The office manager adds them under Outside parties.` : "")}
        </p>
      </div>
      <CaseFields idPrefix="alv-ec-new" kind={kind} values={values} errors={errors} onChange={change} />
      <div className="alv-fu-actions">
        <Button type="submit" variant="primary" disabled={busy}>{busy ? "Saving…" : `Save the ${kindLabel(kind).toLowerCase()}`}</Button>
        <Button type="button" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  );
}
