import { useCallback, useEffect, useMemo, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { Button } from "./Button";
import { FormField } from "./FormField";
import { ConcurrencyConflictBanner } from "./ConcurrencyConflictBanner";
import { EmptyState, ErrorState, LoadingState } from "./StatePatterns";
import { PermissionDenied } from "./PermissionDenied";
import { useNotifications } from "./Notification";
import { confirmDiscard, useUnsavedChangesWarning } from "../hooks/useUnsavedChangesWarning";
import { validateFields } from "./validateFields";
import type { ConfigField, FormValues } from "./validateFields";
export type { ConfigField, FormValues } from "./validateFields";
import { ApiError, isConcurrencyConflict } from "../services/authApi";
import type { ConcurrencyConflictProblem } from "../services/authApi";
import "./ConfigEntityPanel.css";

export interface ConfigColumn<T> {
  header: string;
  render: (row: T) => ReactNode;
}

export interface ConfigRecord {
  id: string;
  isActive: boolean;
  rowVersion: string;
}


interface ConfigEntityPanelProps<T extends ConfigRecord> {
  /** Singular/plural display nouns, e.g. "operatory"/"operatories". */
  noun: string;
  nounPlural: string;
  fields: ConfigField[];
  columns: ConfigColumn<T>[];
  load: (includeInactive: boolean) => Promise<T[]>;
  create: (values: FormValues) => Promise<unknown>;
  update: (row: T, values: FormValues) => Promise<unknown>;
  setActive: (row: T, active: boolean) => Promise<unknown>;
  toValues: (row: T) => FormValues;
  searchText: (row: T) => string;
  permission?: string;
  /** Called with whether the open form has unsaved edits, so a host (tabs) can guard navigation. */
  onDirtyChange?: (dirty: boolean) => void;
  /** When set, creating is blocked and this explains why (e.g. no active location yet). */
  createDisabledReason?: string;
  /** Bump to force a reload when something this panel depends on changed elsewhere. */
  refreshKey?: number;
}

type LoadState<T> = { kind: "loading" } | { kind: "denied" } | { kind: "error" } | { kind: "loaded"; rows: T[] };

/**
 * ALV-N003's reusable configuration UI pattern. A later domain story that owns its own settings
 * (note templates, recall defaults, payment methods...) supplies fields, columns and API calls and
 * inherits, unchanged: truthful loading/empty/error/permission-denied states, search + show-inactive
 * filtering, inline validation, unsaved-change protection, the shared stale-edit conflict banner,
 * server error messages, and inactivate/reactivate instead of delete.
 */
export function ConfigEntityPanel<T extends ConfigRecord>(props: ConfigEntityPanelProps<T>) {
  const { noun, nounPlural, fields, columns, load, create, update, setActive, toValues, searchText, permission, onDirtyChange, createDisabledReason, refreshKey } = props;
  const { notify } = useNotifications();

  const [state, setState] = useState<LoadState<T>>({ kind: "loading" });
  const [includeInactive, setIncludeInactive] = useState(false);
  const [search, setSearch] = useState("");

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<T | null>(null);
  const [initialValues, setInitialValues] = useState<FormValues>({});
  const [values, setValues] = useState<FormValues>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const [saving, setSaving] = useState(false);

  const dirty = formOpen && JSON.stringify(values) !== JSON.stringify(initialValues);
  useUnsavedChangesWarning(dirty);
  useEffect(() => {
    onDirtyChange?.(dirty);
  }, [dirty, onDirtyChange]);

  const refresh = useCallback(async (): Promise<T[] | null> => {
    try {
      const rows = await load(includeInactive);
      setState({ kind: "loaded", rows });
      return rows;
    } catch (err) {
      setState(err instanceof ApiError && err.status === 403 ? { kind: "denied" } : { kind: "error" });
      return null;
    }
  }, [load, includeInactive]);

  useEffect(() => {
    refresh();
  }, [refresh, refreshKey]);

  function openForm(row: T | null) {
    if (!confirmDiscard(dirty)) return;
    const initial = row ? toValues(row) : Object.fromEntries(fields.map((f) => [f.name, ""]));
    setEditing(row);
    setInitialValues(initial);
    setValues(initial);
    setErrors({});
    setFormError(null);
    setConflict(null);
    setFormOpen(true);
  }

  function closeForm() {
    if (!confirmDiscard(dirty)) return;
    setFormOpen(false);
    setEditing(null);
    setConflict(null);
    setFormError(null);
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const found = validateFields(fields, values, editing === null);
    setErrors(found);
    if (Object.keys(found).length > 0) return;

    setSaving(true);
    setFormError(null);
    try {
      if (editing) await update(editing, values);
      else await create(values);
      notify("success", editing ? `Saved changes to the ${noun}.` : `Added the ${noun}.`);
      setFormOpen(false);
      setEditing(null);
      setInitialValues({});
      setValues({});
      await refresh();
    } catch (err) {
      if (isConcurrencyConflict(err)) {
        setConflict(err.body);
      } else if (err instanceof ApiError && err.status === 403) {
        setFormError("You don't have permission to make this change.");
      } else if (err instanceof ApiError) {
        setFormError(err.message);
      } else {
        setFormError(`Could not save the ${noun}. Check your connection and try again.`);
      }
    } finally {
      setSaving(false);
    }
  }

  async function reloadAfterConflict() {
    const rows = await refresh();
    const fresh = rows?.find((r) => r.id === editing?.id) ?? null;
    if (fresh) {
      const next = toValues(fresh);
      setEditing(fresh);
      setInitialValues(next);
      setValues(next);
    } else {
      setFormOpen(false);
      setEditing(null);
    }
    setConflict(null);
    setFormError(null);
  }

  async function toggleActive(row: T) {
    try {
      await setActive(row, !row.isActive);
      notify("success", row.isActive ? `Inactivated the ${noun}.` : `Reactivated the ${noun}.`);
    } catch (err) {
      if (isConcurrencyConflict(err)) {
        notify("warning", `This ${noun} was changed by someone else. The list has been refreshed - try again.`);
      } else {
        notify("danger", err instanceof ApiError ? err.message : `Could not update the ${noun}.`);
      }
    }
    await refresh();
  }

  const visibleRows = useMemo(() => {
    if (state.kind !== "loaded") return [];
    const needle = search.trim().toLowerCase();
    return needle === "" ? state.rows : state.rows.filter((r) => searchText(r).toLowerCase().includes(needle));
  }, [state, search, searchText]);

  const title = editing ? `Edit ${noun}` : `Add ${noun}`;

  return (
    <section className="alv-config-panel" aria-label={nounPlural}>
      {state.kind === "loading" && <LoadingState label={`Loading ${nounPlural}…`} />}
      {state.kind === "denied" && <PermissionDenied requiredPermission={permission} />}
      {state.kind === "error" && (
        <ErrorState
          title={`Could not load ${nounPlural}`}
          action={
            <Button
              onClick={() => {
                setState({ kind: "loading" });
                refresh();
              }}
            >
              Retry
            </Button>
          }
        />
      )}

      {state.kind === "loaded" && (
        <>
          <div className="alv-config-panel__toolbar">
            <div className="alv-config-panel__filters">
              <label className="alv-config-panel__search">
                <span className="alv-visually-hidden">Search {nounPlural}</span>
                <input type="search" placeholder={`Search ${nounPlural}`} value={search} onChange={(e) => setSearch(e.target.value)} />
              </label>
              <label className="alv-config-panel__toggle">
                <input type="checkbox" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
                Show inactive
              </label>
            </div>
            <Button variant="primary" onClick={() => openForm(null)} disabled={!!createDisabledReason}>
              Add {noun}
            </Button>
          </div>
          {createDisabledReason && <p className="alv-config-panel__note">{createDisabledReason}</p>}

          {state.rows.length === 0 && (
            <EmptyState
              title={`No ${nounPlural} yet`}
              description={includeInactive ? undefined : `Add the first ${noun}. Inactive ${nounPlural} are hidden - use "Show inactive" to see them.`}
            />
          )}
          {state.rows.length > 0 && visibleRows.length === 0 && <EmptyState title={`No ${nounPlural} match "${search}"`} />}

          {visibleRows.length > 0 && (
            <table className="alv-config-panel__table">
              <thead>
                <tr>
                  {columns.map((c) => (
                    <th key={c.header}>{c.header}</th>
                  ))}
                  <th>Status</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {visibleRows.map((row) => (
                  <tr key={row.id} className={row.isActive ? undefined : "alv-config-panel__row--inactive"}>
                    {columns.map((c) => (
                      <td key={c.header}>{c.render(row)}</td>
                    ))}
                    <td>
                      <span className={`alv-status-badge${row.isActive ? " alv-status-badge--enabled" : " alv-status-badge--disabled"}`}>
                        {row.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                    <td className="alv-config-panel__actions">
                      <Button onClick={() => openForm(row)} aria-label={`Edit ${searchText(row)}`}>
                        Edit
                      </Button>
                      <Button onClick={() => toggleActive(row)} aria-label={`${row.isActive ? "Inactivate" : "Reactivate"} ${searchText(row)}`}>
                        {row.isActive ? "Inactivate" : "Reactivate"}
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}

      {formOpen && (
        <form className="alv-config-panel__form" onSubmit={handleSubmit} noValidate aria-label={title}>
          <h2 className="alv-config-panel__form-title">{title}</h2>
          {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={reloadAfterConflict} />}
          {fields
            .filter((f) => editing === null || !f.createOnly)
            .map((field) =>
              field.type === "select" ? (
                <div className="alv-form-field" key={field.name}>
                  <label className="alv-form-field__label" htmlFor={`cfg-${field.name}`}>
                    {field.label}
                  </label>
                  <select
                    id={`cfg-${field.name}`}
                    className="alv-form-field__input"
                    value={values[field.name] ?? ""}
                    aria-invalid={errors[field.name] ? true : undefined}
                    aria-describedby={errors[field.name] ? `cfg-${field.name}-error` : undefined}
                    onChange={(e) => setValues({ ...values, [field.name]: e.target.value })}
                  >
                    <option value="">{field.required ? "Select…" : "None"}</option>
                    {(field.optionsFor ? field.optionsFor(editing?.id ?? null) : field.options)?.map((o) => (
                      <option key={o.value} value={o.value}>
                        {o.label}
                      </option>
                    ))}
                  </select>
                  {errors[field.name] && (
                    <p id={`cfg-${field.name}-error`} className="alv-form-field__error" role="alert">
                      {errors[field.name]}
                    </p>
                  )}
                </div>
              ) : (
                <FormField
                  key={field.name}
                  label={field.label}
                  hint={field.hint}
                  error={errors[field.name]}
                  type={field.type === "number" ? "number" : "text"}
                  min={field.min}
                  max={field.max}
                  step={field.step}
                  value={values[field.name] ?? ""}
                  onChange={(e) => setValues({ ...values, [field.name]: e.target.value })}
                />
              )
            )}
          {formError && (
            <p className="alv-form-field__error" role="alert">
              {formError}
            </p>
          )}
          <div className="alv-config-panel__form-actions">
            <Button type="submit" variant="primary" disabled={saving}>
              {saving ? "Saving…" : editing ? "Save changes" : `Add ${noun}`}
            </Button>
            <Button type="button" onClick={closeForm} disabled={saving}>
              Cancel
            </Button>
            {dirty && <span className="alv-config-panel__dirty">Unsaved changes</span>}
          </div>
        </form>
      )}
    </section>
  );
}
