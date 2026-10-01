import { useCallback, useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { PermissionDenied } from "../../components/PermissionDenied";
import { useNotifications } from "../../components/Notification";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import { getPractice, savePractice } from "../../services/configApi";
import type { PracticeInfo } from "../../services/configApi";
import { LocationPanel } from "./SimpleTabs";

type State = { kind: "loading" } | { kind: "denied" } | { kind: "error" } | { kind: "loaded"; info: PracticeInfo };
const toForm = (info: PracticeInfo) => ({ name: info.name ?? "", phone: info.phone ?? "", addressLine: info.addressLine ?? "" });

/**
 * Practice information plus the single active location. Time zone and currency are shown
 * read-only on purpose: they are deployment invariants (ALV-N002) that the clock and Money type
 * actually use, so a second editable copy here could only ever disagree with them.
 */
export function PracticeTab({ onDirtyChange }: { onDirtyChange: (dirty: boolean) => void }) {
  const { notify } = useNotifications();
  const [state, setState] = useState<State>({ kind: "loading" });
  const [form, setForm] = useState({ name: "", phone: "", addressLine: "" });
  const [initial, setInitial] = useState({ name: "", phone: "", addressLine: "" });
  const [nameError, setNameError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const [panelDirty, setPanelDirty] = useState(false);
  const [saving, setSaving] = useState(false);

  const dirty = JSON.stringify(form) !== JSON.stringify(initial);
  useUnsavedChangesWarning(dirty);
  useEffect(() => {
    onDirtyChange(dirty || panelDirty);
  }, [dirty, panelDirty, onDirtyChange]);

  const load = useCallback(async () => {
    setState({ kind: "loading" });
    try {
      const info = await getPractice();
      setState({ kind: "loaded", info });
      setForm(toForm(info));
      setInitial(toForm(info));
      setConflict(null);
      setFormError(null);
    } catch (err) {
      setState(err instanceof ApiError && err.status === 403 ? { kind: "denied" } : { kind: "error" });
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (state.kind !== "loaded") return;
    setFormError(null);
    if (form.name.trim() === "") {
      setNameError("Practice name is required.");
      return;
    }
    setNameError(null);
    setSaving(true); // pending-write policy: inputs are disabled until the response, so it can never overwrite newer typing
    try {
      const info = await savePractice({ ...form, name: form.name.trim(), rowVersion: state.info.rowVersion });
      setState({ kind: "loaded", info });
      setForm(toForm(info));
      setInitial(toForm(info));
      notify("success", "Practice information saved.");
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else setFormError(err instanceof ApiError ? err.message : "Could not save. Check your connection and try again.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="alv-config-panel">
      {state.kind === "loading" && <LoadingState label="Loading practice information…" />}
      {state.kind === "denied" && <PermissionDenied requiredPermission="ManagePracticeConfiguration" />}
      {state.kind === "error" && <ErrorState title="Could not load practice information" action={<Button onClick={load}>Retry</Button>} />}

      {state.kind === "loaded" && (
        <form className="alv-config-panel__form" onSubmit={submit} noValidate aria-label="Practice information">
          <h2 className="alv-config-panel__form-title">Practice information</h2>
          {!state.info.configured && <p className="alv-config-panel__note">The practice has not been named yet.</p>}
          {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={load} />}
          <fieldset className="alv-config-panel__fields" disabled={saving}>
          <FormField label="Practice name" value={form.name} maxLength={120} error={nameError ?? undefined} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <FormField label="Phone" value={form.phone} maxLength={40} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
          <FormField label="Address" value={form.addressLine} maxLength={200} onChange={(e) => setForm({ ...form, addressLine: e.target.value })} />
          <FormField label="Time zone" value={state.info.timeZoneId} readOnly hint="Set by the deployment so every appointment time converts consistently." />
          <FormField label="Currency" value={state.info.currency} readOnly hint="Fixed for the first release." />
          </fieldset>
          {formError && (
            <p className="alv-form-field__error" role="alert">
              {formError}
            </p>
          )}
          <div className="alv-config-panel__form-actions">
            <Button type="submit" variant="primary" disabled={!dirty || saving}>
              Save practice information
            </Button>
            {dirty && <span className="alv-config-panel__dirty">Unsaved changes</span>}
          </div>
        </form>
      )}

      <h2 className="alv-config-panel__form-title">Location</h2>
      <LocationPanel onDirtyChange={setPanelDirty} />
    </div>
  );
}
