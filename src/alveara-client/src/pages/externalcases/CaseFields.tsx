import type { CaseKind } from "../../services/externalCasesApi";
import type { CaseFormValues } from "./caseFormValues";

interface Props { idPrefix: string; kind: CaseKind; values: CaseFormValues; errors: Record<string, string>; onChange: (next: CaseFormValues) => void }

/** The fields every case form shares: what it is for, when it is due, notes, and for a lab case the tooth, material and shade. */
export function CaseFields({ idPrefix, kind, values, errors, onChange }: Props) {
  const set = (name: keyof CaseFormValues) => (e: { target: { value: string } }) => onChange({ ...values, [name]: e.target.value });
  const field = (name: keyof CaseFormValues, label: string, hint?: string, multiline = false) => (
    <div className="alv-form-field">
      <label htmlFor={`${idPrefix}-${name}`} className="alv-form-field__label">{label}</label>
      {multiline
        ? <textarea id={`${idPrefix}-${name}`} rows={3} value={values[name]} onChange={set(name)} aria-invalid={!!errors[name]} aria-describedby={`${idPrefix}-${name}-msg`} />
        : <input id={`${idPrefix}-${name}`} className="alv-form-field__input" type={name === "dueOn" ? "date" : "text"} value={values[name]} onChange={set(name)} aria-invalid={!!errors[name]} aria-describedby={`${idPrefix}-${name}-msg`} autoComplete="off" />}
      <p id={`${idPrefix}-${name}-msg`} className={errors[name] ? "alv-fu-error" : "alv-fu-item__meta"}>{errors[name] ?? hint ?? ""}</p>
    </div>
  );
  return (
    <>
      {field("summary", kind === "Lab" ? "What is being made *" : "Reason for the referral *", kind === "Lab" ? "For example: Crown, zirconia." : undefined)}
      {field("dueOn", "Follow up by (optional)", "The date the practice will chase it. A task appears in the work queue from that day if it has not come back.")}
      {kind === "Lab" && (
        <>
          {field("toothKey", "Tooth (optional)", "Its two-digit number, for example 36.")}
          {field("material", "Material (optional)")}
          {field("shade", "Shade (optional)")}
        </>
      )}
      {field("notes", "Notes (optional)", undefined, true)}
    </>
  );
}
