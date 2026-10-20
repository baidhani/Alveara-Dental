import { useId, useState } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import type { CategoryView, LibraryFilter } from "../../services/documentsApi";
import type { EncounterChoice } from "../../services/documentsApi";
import { REGIONS, REGION_LABELS } from "../diagnosis/diagnosisStructureRules";
import { TEETH, toothOptionLabel } from "./documentContext";
import { encounterLabel } from "./documentText";
import { EMPTY_DRAFT, toFilter } from "./documentFilter";
import type { Draft, FilterErrors } from "./documentFilter";

/**
 * ALV-010-C01: narrowing a patient's library. Nothing is sent until "Apply filters" is pressed; a tooth or a region (never both); a filter the server refuses is named beside its box and what was typed stays.
 * By default only active documents are shown; inactive ones (hidden, never deleted) are asked for with "Show".
 */
export function DocumentFilters({ categories, encounters, errors, onApply }: { categories: CategoryView[]; encounters: EncounterChoice[]; errors: FilterErrors; onApply: (filter: LibraryFilter) => void }) {
  const id = useId();
  const [draft, setDraft] = useState<Draft>(EMPTY_DRAFT);
  const set = (patch: Partial<Draft>) => setDraft((d) => ({ ...d, ...patch }));
  const describe = (name: keyof FilterErrors) => (errors[name] ? `${id}-${name}-error` : undefined);
  const message = (name: keyof FilterErrors) => errors[name] && <p id={`${id}-${name}-error`} className="alv-form-field__error" role="alert">{errors[name]}</p>;
  const ordered = [...encounters].sort((a, b) => b.encounterAtUtc.localeCompare(a.encounterAtUtc));

  return (
    <form role="search" aria-label="Filter documents" className="alv-documents__filters" noValidate onSubmit={(e) => { e.preventDefault(); onApply(toFilter(draft)); }}>
      <div className="alv-form-field">
        <label htmlFor={`${id}-category`} className="alv-form-field__label">Category</label>
        <select id={`${id}-category`} className="alv-form-field__input" value={draft.categoryId} aria-invalid={errors.category ? true : undefined} aria-describedby={describe("category")} onChange={(e) => set({ categoryId: e.target.value })}>
          <option value="">All categories</option>
          {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        {message("category")}
      </div>
      <div className="alv-form-field">
        <label htmlFor={`${id}-encounter`} className="alv-form-field__label">Encounter</label>
        <select id={`${id}-encounter`} className="alv-form-field__input" value={draft.encounterId} aria-invalid={errors.encounter ? true : undefined} aria-describedby={describe("encounter")} onChange={(e) => set({ encounterId: e.target.value })}>
          <option value="">Any encounter</option>
          {ordered.map((e) => <option key={e.id} value={e.id}>{encounterLabel(e.encounterAtUtc)}</option>)}
        </select>
        {message("encounter")}
      </div>
      <div className="alv-form-field">
        <label htmlFor={`${id}-tooth`} className="alv-form-field__label">Tooth</label>
        <select id={`${id}-tooth`} className="alv-form-field__input" value={draft.toothKey} aria-invalid={errors.toothKey ? true : undefined} aria-describedby={describe("toothKey")}
          onChange={(e) => set({ toothKey: e.target.value, regionKey: e.target.value ? "" : draft.regionKey })}>
          <option value="">Any tooth</option>
          {TEETH.map((k) => <option key={k} value={k}>{toothOptionLabel(k)}</option>)}
        </select>
        {message("toothKey")}
      </div>
      <div className="alv-form-field">
        <label htmlFor={`${id}-region`} className="alv-form-field__label">Oral region</label>
        <select id={`${id}-region`} className="alv-form-field__input" value={draft.regionKey} aria-invalid={errors.regionKey ? true : undefined} aria-describedby={describe("regionKey")}
          onChange={(e) => set({ regionKey: e.target.value, toothKey: e.target.value ? "" : draft.toothKey })}>
          <option value="">Any region</option>
          {REGIONS.map((r) => <option key={r} value={r}>{REGION_LABELS[r]}</option>)}
        </select>
        {message("regionKey")}
      </div>
      <FormField label="From" type="date" value={draft.from} error={errors.from} onChange={(e) => set({ from: e.target.value })} />
      <FormField label="To" type="date" value={draft.to} error={errors.to} onChange={(e) => set({ to: e.target.value })} />
      <FormField label="Search file names and descriptions" type="search" value={draft.text} error={errors.text} onChange={(e) => set({ text: e.target.value })} />
      <div className="alv-form-field">
        <label htmlFor={`${id}-status`} className="alv-form-field__label">Show</label>
        <select id={`${id}-status`} className="alv-form-field__input" value={draft.status} aria-invalid={errors.status ? true : undefined} aria-describedby={describe("status")} onChange={(e) => set({ status: e.target.value })}>
          <option value="Active">Active documents</option>
          <option value="Inactive">Inactive documents</option>
          <option value="All">All documents</option>
        </select>
        {message("status")}
      </div>
      <div className="alv-documents__actions alv-documents__wide">
        <Button type="submit" variant="primary">Apply filters</Button>
        <Button type="button" onClick={() => { setDraft(EMPTY_DRAFT); onApply({}); }}>Clear filters</Button>
      </div>
    </form>
  );
}
