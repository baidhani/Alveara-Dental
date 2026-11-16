import { useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { useNotifications } from "../../components/Notification";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import { saveVisitWorkflow } from "../../services/configApi";
import type { PracticeInfo } from "../../services/configApi";

const fromInfo = (info: PracticeInfo) => ({ confirmationRequired: info.confirmationRequired ?? true, markReadyRequired: info.markReadyRequired ?? true });

/**
 * ALV-N021 A2 (OD-03, OD-04): the two visit-workflow settings. Both default to required. A change applies from the next visit move on;
 * moves already made stay as they are. Shown only once the practice exists (the settings live on the practice row). The parent remounts the panel
 * (key = the practice row version) when the saved settings change, so the form starts from them without copying props into state in an effect.
 */
export function VisitWorkflowPanel({ info, onSaved, onReload }: { info: PracticeInfo; onSaved: (info: PracticeInfo) => void; onReload: () => void }) {
  const { notify } = useNotifications();
  const [form, setForm] = useState(fromInfo(info));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const dirty = JSON.stringify(form) !== JSON.stringify(fromInfo(info));

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSaving(true);
    try {
      onSaved(await saveVisitWorkflow({ ...form, rowVersion: info.rowVersion }));
      notify("success", "Visit workflow saved. It applies from the next move.");
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else setError(err instanceof ApiError ? err.message : "Could not save. Check your connection and try again.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <form className="alv-config-panel__form" onSubmit={submit} noValidate aria-label="Visit workflow">
      <h2 className="alv-config-panel__form-title">Visit workflow</h2>
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={onReload} />}
      <fieldset className="alv-config-panel__fields" disabled={saving}>
        <legend className="alv-config-panel__note">Changes apply from the next move; visits already moved stay as they are.</legend>
        <label style={{ display: "block" }}>
          <input type="checkbox" checked={form.confirmationRequired} onChange={(e) => setForm({ ...form, confirmationRequired: e.target.checked })} />
          Confirm before check-in (when off, a scheduled patient can be checked in directly)
        </label>
        <label style={{ display: "block" }}>
          <input type="checkbox" checked={form.markReadyRequired} onChange={(e) => setForm({ ...form, markReadyRequired: e.target.checked })} />
          Mark ready before seating (when off, a checked-in patient can be seated directly)
        </label>
      </fieldset>
      {error && <p className="alv-form-field__error" role="alert">{error}</p>}
      <div className="alv-config-panel__form-actions">
        <Button type="submit" variant="primary" disabled={!dirty || saving}>Save visit workflow</Button>
        {dirty && <span className="alv-config-panel__dirty">Unsaved changes</span>}
      </div>
    </form>
  );
}
