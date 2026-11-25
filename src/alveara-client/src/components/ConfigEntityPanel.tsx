import { useCallback, useEffect, useMemo, useRef, useState } from "react";
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

/** ALV-N030-C01: a field locked by a ticked checkbox (see ConfigField.lockedBy). */
function isLocked(field: ConfigField, values: FormValues): boolean {
  return field.lockedBy !== undefined && values[field.lockedBy.field] === "true";
}
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
  /** ALV-N020: asked before an inactivate/reactivate is sent; resolve false to cancel (e.g. an in-app confirmation that the login will be disabled). */
  beforeSetActive?: (row: T, active: boolean) => Promise<boolean>;
  /** ALV-N020: extra actions shown in a row after Edit and Inactivate/Reactivate (e.g. unlink a login from an inactive staff profile). */
  rowActions?: (row: T, reload: () => Promise<void>) => ReactNode;
  /** ALV-N030-C01 (brief 18.8): when this returns a reason, the row's Inactivate/Reactivate button is disabled and the reason is shown beside it. */
  activeToggleDisabledReason?: (row: T) => string | undefined;
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
  const { noun, nounPlural, fields, columns, load, create, update, setActive, toValues, searchText, permission, onDirtyChange, createDisabledReason, refreshKey, beforeSetActive, rowActions, activeToggleDisabledReason } = props;
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
  const [reloadError, setReloadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  /**
   * ALV-N003 R03: the editing-session generation. It advances every time the form is opened for a
   * record or closed, and every async completion (save, conflict reload) is discarded unless it still
   * belongs to the session that issued it - a provider/record id alone cannot tell an abandoned
   * session from a new one on the same record. While a save is in flight the form's inputs and the
   * other rows' Edit buttons are disabled (the explicit pending-write policy), so nothing typed
   * after the submitted snapshot can be overwritten by its response.
   */
  const formSession = useRef(0);

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
    formSession.current += 1;
    const initial = row ? toValues(row) : Object.fromEntries(fields.map((f) => [f.name, ""]));
    setEditing(row);
    setInitialValues(initial);
    setValues(initial);
    setErrors({});
    setFormError(null);
    setConflict(null);
    setReloadError(null);
    setFormOpen(true);
  }

  function closeForm() {
    if (!confirmDiscard(dirty)) return;
    formSession.current += 1;
    setFormOpen(false);
    setEditing(null);
    setConflict(null);
    setReloadError(null);
    setFormError(null);
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const found = validateFields(fields, values, editing === null);
    setErrors(found);
    if (Object.keys(found).length > 0) return;

    const session = formSession.current;
    setSaving(true);
    setFormError(null);
    try {
      if (editing) await update(editing, values);
      else await create(values);
      if (session !== formSession.current) {
        await refresh(); // the write committed, so the list is stale - but the form it came from is gone: no UI feedback
        return;
      }
      notify("success", editing ? `Saved changes to the ${noun}.` : `Added the ${noun}.`);
      formSession.current += 1;
      setFormOpen(false);
      setEditing(null);
      setInitialValues({});
      setValues({});
      await refresh();
    } catch (err) {
      if (session !== formSession.current) return;
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
      setSaving(false); // forms cannot be opened or closed while saving, so this always belongs to the form that started it
    }
  }

  /**
   * ALV-N003 R02 (review finding ALV-N003-R01-05): recovery replaces the user's draft ONLY after a
   * valid fresh record has actually been obtained. A failed read leaves the form, the draft and the
   * conflict exactly as they were, with a retryable error - it is not the same thing as "the record
   * no longer exists".
   */
  async function reloadAfterConflict() {
    const id = editing?.id;
    const session = formSession.current;
    setReloadError(null);
    let fresh: T | null;
    try {
      let rows = await load(includeInactive);
      setState({ kind: "loaded", rows });
      fresh = rows.find((r) => r.id === id) ?? null;
      if (!fresh && !includeInactive) {
        // Not in the active list: it may have been inactivated by whoever changed it. Look there before concluding it is gone.
        rows = await load(true);
        fresh = rows.find((r) => r.id === id) ?? null;
      }
    } catch (err) {
      if (session !== formSession.current) return;
      setReloadError(err instanceof ApiError ? err.message : `Could not reload the ${noun}. Check your connection and try again.`);
      return; // keep the draft, the conflict banner and the form
    }
    if (session !== formSession.current) return; // the user closed or switched forms meanwhile: never touch the new session

    if (fresh) {
      formSession.current += 1; // the editor is repopulated with the other editor's version: a new session
      const next = toValues(fresh);
      setEditing(fresh);
      setInitialValues(next);
      setValues(next);
      setConflict(null);
      setFormError(null);
    } else {
      // Genuinely gone (the read succeeded and it is in neither list): nothing left to edit.
      notify("warning", `This ${noun} no longer exists. Your changes could not be saved.`);
      setFormOpen(false);
      setEditing(null);
      setConflict(null);
      setFormError(null);
    }
  }

  async function toggleActive(row: T) {
    if (beforeSetActive && !(await beforeSetActive(row, !row.isActive))) return;
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
            <Button variant="primary" onClick={() => openForm(null)} disabled={!!createDisabledReason || saving}>
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
                      <Button onClick={() => openForm(row)} disabled={saving} aria-label={`Edit ${searchText(row)}`}>
                        Edit
                      </Button>
                      <Button onClick={() => toggleActive(row)} aria-label={`${row.isActive ? "Inactivate" : "Reactivate"} ${searchText(row)}`}
                        disabled={activeToggleDisabledReason?.(row) !== undefined}>
                        {row.isActive ? "Inactivate" : "Reactivate"}
                      </Button>
                      {activeToggleDisabledReason?.(row) && <span className="alv-form-field__hint">{activeToggleDisabledReason(row)}</span>}
                      {rowActions?.(row, async () => { await refresh(); })}
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
          {conflict && reloadError && (
            <p className="alv-form-field__error" role="alert">
              {reloadError} Your edits are still here - use "Reload current version" to try again.
            </p>
          )}
          <fieldset className="alv-config-panel__fields" disabled={saving}>
          {fields
            .filter((f) => editing === null || !f.createOnly)
            .map((field) =>
              field.type === "checkbox" ? (
                <div className="alv-form-field alv-form-field--checkbox" key={field.name}>
                  <label className="alv-form-field__label" htmlFor={`cfg-${field.name}`}>
                    <input
                      id={`cfg-${field.name}`}
                      type="checkbox"
                      checked={values[field.name] === "true"}
                      aria-describedby={field.hint ? `cfg-${field.name}-hint` : undefined}
                      onChange={(e) => {
                        const next = { ...values, [field.name]: e.target.checked ? "true" : "" };
                        if (e.target.checked) for (const f of fields) if (f.lockedBy?.field === field.name) next[f.name] = f.lockedBy.value;
                        setValues(next);
                      }}
                    />{" "}
                    {field.label}
                  </label>
                  {field.hint && <p id={`cfg-${field.name}-hint`} className="alv-form-field__hint">{field.hint}</p>}
                </div>
              ) : field.type === "select" ? (
                <div className="alv-form-field" key={field.name}>
                  <label className="alv-form-field__label" htmlFor={`cfg-${field.name}`}>
                    {field.label}
                  </label>
                  <select
                    id={`cfg-${field.name}`}
                    className="alv-form-field__input"
                    value={values[field.name] ?? ""}
                    disabled={isLocked(field, values)}
                    aria-invalid={errors[field.name] ? true : undefined}
                    aria-describedby={errors[field.name] ? `cfg-${field.name}-error` : isLocked(field, values) ? `cfg-${field.name}-locked` : undefined}
                    onChange={(e) => setValues({ ...values, [field.name]: e.target.value })}
                  >
                    <option value="">{field.required ? "Select…" : "None"}</option>
                    {(field.optionsFor ? field.optionsFor(editing?.id ?? null) : field.options)?.map((o) => (
                      <option key={o.value} value={o.value}>
                        {o.label}
                      </option>
                    ))}
                  </select>
                  {isLocked(field, values) && <p id={`cfg-${field.name}-locked`} className="alv-form-field__hint">{field.lockedBy!.hint}</p>}
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
          </fieldset>
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
