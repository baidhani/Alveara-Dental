import { useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { CATEGORIES, CODE_SYSTEMS, DENTITIONS, SCOPES } from "../../services/proceduresApi";
import type { ProcedureInput } from "../../services/proceduresApi";
import type { SaveResult } from "../safety/SafetyForms";
import { CATEGORY_TEXT, CODE_SYSTEM_TEXT, DENTITION_TEXT, SCOPE_TEXT, isToothLevel, parseFee } from "./procedureText";

interface Props {
  initial: ProcedureInput;
  /** Present when changing an existing procedure: its code and code system are then fixed, and a reason is required. */
  revising?: { codeSystemText: string; code: string };
  busy: boolean;
  submitLabel: string;
  onSubmit: (input: ProcedureInput, reason: string) => Promise<SaveResult>;
  onCancel: () => void;
}

/**
 * ALV-N005: the fields of one procedure version. Adding asks for the code system, code, description, category, where it applies and the fee; changing keeps the code fixed (it is the
 * procedure's identity) and asks why. Nothing is pre-filled with a guess, every refusal is shown beside its field, and typed values survive a refused or failed save.
 */
export function ProcedureForm({ initial, revising, busy, submitLabel, onSubmit, onCancel }: Props) {
  const [values, setValues] = useState<ProcedureInput>(initial);
  const [feeText, setFeeText] = useState(initial.fee === null ? "" : initial.fee.toFixed(2));
  const [reason, setReason] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const ids = { system: useId(), category: useId(), scope: useId(), dentition: useId(), reason: useId() };
  const set = (patch: Partial<ProcedureInput>) => setValues((v) => ({ ...v, ...patch }));
  useUnsavedChangesWarning(JSON.stringify(values) !== JSON.stringify(initial) || feeText !== (initial.fee === null ? "" : initial.fee.toFixed(2)) || reason.trim() !== "");
  const needsSource = values.codeSystem !== "Local";

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    const found: Record<string, string> = {};
    if (!revising && values.code.trim() === "") found.code = "A code is required.";
    if (values.description.trim() === "") found.description = "A description is required.";
    if (values.category === "") found.category = "Choose a category.";
    if (values.scope === "") found.scope = "Choose where it applies.";
    if (needsSource && values.sourceName.trim() === "") found.sourceName = "Name the source of this code set.";
    if (revising && reason.trim() === "") found.reason = "Say why.";
    const fee = parseFee(feeText);
    if ("error" in fee) found.fee = fee.error;
    setErrors(found);
    if (Object.keys(found).length > 0 || "error" in fee) return;
    const refused = await onSubmit({ ...values, fee: fee.fee }, reason.trim());
    if (refused) setErrors(refused);
  }

  const select = (id: string, label: string, field: "codeSystem" | "category" | "scope" | "dentition", options: Record<string, string>, list: readonly string[], blank: boolean) => (
    <div className="alv-form-field">
      <label htmlFor={id} className="alv-form-field__label">{label}</label>
      <select id={id} className="alv-form-field__input" value={values[field]} aria-invalid={errors[field] ? true : undefined} onChange={(e) => set({ [field]: e.target.value })}>
        {blank && <option value="">Choose…</option>}
        {list.map((o) => <option key={o} value={o}>{options[o]}</option>)}
      </select>
      {errors[field] && <p className="alv-form-field__error" role="alert">{errors[field]}</p>}
    </div>
  );

  return (
    <form className="alv-safety__form" onSubmit={submit} noValidate aria-label={revising ? `Change procedure ${revising.code}` : "Add a procedure"}>
      {revising
        ? <p className="alv-clinical__meta">{revising.codeSystemText} <strong>{revising.code}</strong> - the code cannot be changed. To use a different code, add a new procedure.</p>
        : <>
            {select(ids.system, "Code system", "codeSystem", CODE_SYSTEM_TEXT, CODE_SYSTEMS, false)}
            <FormField label="Code" value={values.code} error={errors.code} maxLength={20} hint="It cannot be changed later. A practice code must not look like a CDT code (D followed by four digits)." onChange={(e) => set({ code: e.target.value.toUpperCase() })} autoFocus />
          </>}
      <FormField label="Description" value={values.description} error={errors.description} maxLength={200} onChange={(e) => set({ description: e.target.value })} />
      {select(ids.category, "Category", "category", CATEGORY_TEXT, CATEGORIES, true)}
      {select(ids.scope, "Applies to", "scope", SCOPE_TEXT, SCOPES, true)}
      {isToothLevel(values.scope) && select(ids.dentition, "Teeth", "dentition", DENTITION_TEXT, DENTITIONS, false)}
      <FormField label="Fee (US dollars)" value={feeText} error={errors.fee} inputMode="decimal" hint="Enter 0 for no charge." onChange={(e) => setFeeText(e.target.value)} />
      {needsSource && (
        <>
          <FormField label="Source of the code set" value={values.sourceName} error={errors.sourceName} maxLength={80} onChange={(e) => set({ sourceName: e.target.value })} />
          <FormField label="Edition or version (optional)" value={values.sourceVersion} error={errors.sourceVersion} maxLength={40} onChange={(e) => set({ sourceVersion: e.target.value })} />
        </>
      )}
      <FormField label={revising ? "Takes effect on (leave blank for today)" : "Starts on (leave blank for today)"} type="date" value={values.effectiveFrom} error={errors.effectiveFrom} onChange={(e) => set({ effectiveFrom: e.target.value })} />
      <FormField label="Last valid date (optional)" type="date" value={values.validThrough} error={errors.validThrough} onChange={(e) => set({ validThrough: e.target.value })} />
      {revising && (
        <div className="alv-form-field">
          <label htmlFor={ids.reason} className="alv-form-field__label">Why is this being changed?</label>
          <textarea id={ids.reason} className="alv-form-field__input alv-clinical__details" rows={2} maxLength={500} value={reason} aria-invalid={errors.reason ? true : undefined} onChange={(e) => setReason(e.target.value)} />
          {errors.reason && <p className="alv-form-field__error" role="alert">{errors.reason}</p>}
        </div>
      )}
      <div className="alv-clinical__row-actions">
        <Button type="submit" variant="primary" disabled={busy}>{submitLabel}</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}
