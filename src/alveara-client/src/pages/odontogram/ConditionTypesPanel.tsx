import { useEffect, useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { CONDITION_DENTITIONS, CONDITION_SCOPES, TOOTH_EFFECTS, createConditionType, getConditionTypeHistory, reactivateConditionType, retireConditionType } from "../../services/odontogramApi";
import type { ConditionType, ConditionTypeEvent, ConditionTypeInput } from "../../services/odontogramApi";
import { ReasonForm } from "../safety/SafetyForms";
import type { SaveResult } from "../safety/SafetyForms";
import { when } from "./odontogramText";

export type RunTypes = (change: () => Promise<ConditionType[]>, savedText: string) => Promise<SaveResult>;

const SCOPE_TEXT: Record<string, string> = { Surface: "one surface", WholeTooth: "the whole tooth" };
const DENTITION_TEXT: Record<string, string> = { Permanent: "permanent teeth", Primary: "primary teeth", Both: "permanent and primary teeth" };
const EFFECT_TEXT: Record<string, string> = { None: "", Absent: "marks the tooth as missing", Replacement: "stands in for a missing tooth" };
const SCOPE_OPTIONS: Record<string, string> = { Surface: "Surface - recorded on one surface", WholeTooth: "Whole tooth - applies to the tooth as a whole" };
const DENTITION_OPTIONS: Record<string, string> = { Permanent: "Permanent teeth only", Primary: "Primary teeth only", Both: "Permanent and primary teeth" };
const EFFECT_OPTIONS: Record<string, string> = { None: "None - says nothing about whether the tooth is there", Absent: "Absent - the tooth is missing", Replacement: "Replacement - something stands in for the tooth (such as an implant)" };
const CODE_SHAPE = /^[A-Z][A-Za-z0-9]{1,31}$/;
const suggestCode = (label: string) => label.split(/[^A-Za-z0-9]+/).filter(Boolean).map((w) => w[0].toUpperCase() + w.slice(1)).join("").slice(0, 32);

function ConditionHistory({ type }: { type: ConditionType }) {
  const [open, setOpen] = useState(false);
  const [events, setEvents] = useState<ConditionTypeEvent[] | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    getConditionTypeHistory(type.id, controller.signal).then((e) => { setFailed(false); setEvents(e); }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [open, type.id, type.rowVersion]);
  return (
    <details className="alv-clinical__history" onToggle={(e) => setOpen((e.currentTarget as HTMLDetailsElement).open)}>
      <summary aria-label={`History of the ${type.label} condition`}>History</summary>
      {open && failed && <p className="alv-form-field__error" role="alert">Could not load the history. Close it and open it again.</p>}
      {open && !failed && events === null && <p className="alv-clinical__meta">Loading the history…</p>}
      {open && events && (
        <ol className="alv-clinical__history-list">
          {events.map((e) => (
            <li key={e.eventNumber}><span className="alv-clinical__history-what">{e.changeType}</span>{e.reason && <span> · Reason: {e.reason}</span>}<span className="alv-clinical__meta"> {when(e.occurredAtUtc)} by {e.actorName ?? "Staff member"}</span></li>
          ))}
        </ol>
      )}
    </details>
  );
}

function ConditionRow({ type, canManage, busy, run }: { type: ConditionType; canManage: boolean; busy: boolean; run: RunTypes }) {
  const [mode, setMode] = useState<"view" | "retire" | "reactivate">("view");
  const effect = EFFECT_TEXT[type.toothEffect];
  return (
    <li className="alv-odonto__condition">
      <p className="alv-odonto__finding-title">
        <strong>{type.label}</strong>
        <span className="alv-odonto__chip">{type.isActive ? "Active" : "Retired"}</span>
      </p>
      <p className="alv-odonto__finding-meta">
        Code {type.code} · recorded on {SCOPE_TEXT[type.scope]} · for {DENTITION_TEXT[type.appliesTo]}{effect ? ` · ${effect}` : ""} · added by {type.createdByName ?? "Staff member"} on {when(type.createdAtUtc)}.
      </p>
      {canManage && mode === "view" && (
        <div className="alv-clinical__row-actions">
          {type.isActive
            ? <Button type="button" onClick={() => setMode("retire")} disabled={busy} aria-label={`Retire the ${type.label} condition`}>Retire</Button>
            : <Button type="button" onClick={() => setMode("reactivate")} disabled={busy} aria-label={`Reactivate the ${type.label} condition`}>Reactivate</Button>}
        </div>
      )}
      {mode === "retire" && (
        <ReasonForm label={`Why is "${type.label}" being retired?`} hint="New findings can no longer use it. Findings already recorded keep it, and it stays in the list." required submitLabel="Retire condition" ariaLabel={`Retire condition: ${type.label}`} busy={busy}
          onCancel={() => setMode("view")}
          onSubmit={async (reason) => { const refused = await run(() => retireConditionType(type.id, reason, type.rowVersion), `${type.label} retired.`); if (!refused) setMode("view"); return refused; }} />
      )}
      {mode === "reactivate" && (
        <ReasonForm label={`Why is "${type.label}" being reactivated? (optional)`} required={false} submitLabel="Reactivate condition" ariaLabel={`Reactivate condition: ${type.label}`} busy={busy}
          onCancel={() => setMode("view")}
          onSubmit={async (reason) => { const refused = await run(() => reactivateConditionType(type.id, reason, type.rowVersion), `${type.label} reactivated.`); if (!refused) setMode("view"); return refused; }} />
      )}
      <ConditionHistory type={type} />
    </li>
  );
}

function ConditionTypeForm({ busy, onSubmit, onCancel }: { busy: boolean; onSubmit: (input: ConditionTypeInput) => Promise<SaveResult>; onCancel: () => void }) {
  const [values, setValues] = useState<ConditionTypeInput>({ code: "", label: "", scope: "", appliesTo: "", toothEffect: "None" });
  const [codeTouched, setCodeTouched] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const scopeId = useId();
  const dentitionId = useId();
  const effectId = useId();
  useUnsavedChangesWarning(values.label.trim() !== "" || values.code.trim() !== "");
  const set = (patch: Partial<ConditionTypeInput>) => setValues((v) => ({ ...v, ...patch }));

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    const found: Record<string, string> = {};
    if (values.label.trim() === "") found.label = "A label is required.";
    if (!CODE_SHAPE.test(values.code.trim())) found.code = "Use 2 to 32 letters and digits, starting with a capital letter (for example Fracture).";
    if (values.scope === "") found.scope = "Choose where it is recorded.";
    if (values.appliesTo === "") found.appliesTo = "Choose which teeth it applies to.";
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    const refused = await onSubmit({ ...values, code: values.code.trim(), label: values.label.trim() });
    if (refused) setErrors(refused);
  }

  const select = (id: string, label: string, field: keyof ConditionTypeInput, options: Record<string, string>, list: readonly string[], blank: boolean) => (
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
    <form className="alv-safety__form" onSubmit={submit} noValidate aria-label="Add a condition type">
      <FormField label="Label" value={values.label} error={errors.label} maxLength={80} onChange={(e) => set({ label: e.target.value, ...(codeTouched ? {} : { code: suggestCode(e.target.value) }) })} autoFocus />
      <FormField label="Code" value={values.code} error={errors.code} maxLength={32} hint="What a finding stores. It cannot be changed later." onChange={(e) => { setCodeTouched(true); set({ code: e.target.value }); }} />
      {select(scopeId, "Recorded on", "scope", SCOPE_OPTIONS, CONDITION_SCOPES, true)}
      {select(dentitionId, "Applies to", "appliesTo", DENTITION_OPTIONS, CONDITION_DENTITIONS, true)}
      {select(effectId, "Effect on the tooth", "toothEffect", EFFECT_OPTIONS, TOOTH_EFFECTS, false)}
      <p className="alv-form-field__hint">Once added, a condition's label, code and meaning cannot be changed; a condition that is no longer wanted is retired instead.</p>
      <div className="alv-clinical__row-actions">
        <Button type="submit" variant="primary" disabled={busy}>Add condition type</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}

/**
 * ALV-006-C01: the practice's catalogue of conditions a finding can be recorded as. Everyone who reads the chart can see it; people who manage clinical templates can add a condition, retire
 * one that is no longer wanted (with a reason) and reactivate it. A condition cannot be edited once it exists - retiring stops NEW findings of that kind and every existing finding keeps
 * reading as it did. The list is the server's, so a screen never has a list of its own to fall out of step.
 */
export function ConditionTypesPanel({ types, canManage, busy, run }: { types: ConditionType[] | null; canManage: boolean; busy: boolean; run: RunTypes }) {
  const [adding, setAdding] = useState(false);
  if (types === null) return <p className="alv-clinical__meta">The condition catalogue could not be loaded, so findings cannot be recorded now. Reload the page to try again.</p>;
  const active = types.filter((t) => t.isActive).length;
  return (
    <details className="alv-odonto__catalogue">
      <summary>Condition types ({active} active{types.length > active ? `, ${types.length - active} retired` : ""})</summary>
      <p className="alv-clinical__meta">The conditions a finding can be recorded as. Retiring one stops new findings of that kind; findings already recorded keep it.</p>
      <ul className="alv-clinical__entries" aria-label="Condition types">
        {types.map((t) => <ConditionRow key={t.id} type={t} canManage={canManage} busy={busy} run={run} />)}
      </ul>
      {canManage && (adding
        ? <ConditionTypeForm busy={busy} onCancel={() => setAdding(false)} onSubmit={async (input) => { const refused = await run(() => createConditionType(input), `${input.label} added to the catalogue.`); if (!refused) setAdding(false); return refused; }} />
        : <div className="alv-clinical__section-actions"><Button type="button" onClick={() => setAdding(true)} disabled={busy}>Add a condition type</Button></div>)}
    </details>
  );
}
