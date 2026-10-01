import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { FormField } from "../../components/FormField";
import { useNotifications } from "../../components/Notification";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import { saveBackupSettings } from "../../services/backupApi";
import type { BackupSettings } from "../../services/backupApi";
import { describeBackupError } from "./backupMessages";
import { toForm, validateSettings } from "./settingsRules";
import type { FormState } from "./settingsRules";

/** Schedule, retention, destination and the verification (probation) policy. Edits are protected against being lost and a stale save is a visible conflict. */
export function BackupSettingsForm({ settings, onSaved }: { settings: BackupSettings; onSaved: () => void }) {
  const { notify } = useNotifications();
  const [form, setForm] = useState<FormState>(() => toForm(settings));
  const [baseline, setBaseline] = useState<FormState>(() => toForm(settings));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const [saving, setSaving] = useState(false);

  const dirty = JSON.stringify(form) !== JSON.stringify(baseline);
  useUnsavedChangesWarning(dirty);

  // When the saved settings change underneath a CLEAN form (another reload, another admin), follow them; never overwrite an edit in progress.
  useEffect(() => {
    if (!dirty) {
      const next = toForm(settings);
      setForm(next);
      setBaseline(next);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [settings.rowVersion]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFormError(null);
    const found = validateSettings(form, settings.recoveryKeyConfigured);
    setErrors(found);
    if (Object.keys(found).length > 0) return;

    setSaving(true);
    try {
      await saveBackupSettings({
        scheduleEnabled: form.scheduleEnabled,
        scheduleIntervalHours: Number(form.interval),
        retentionCount: Number(form.retention),
        destinationDirectory: form.destination.trim() === "" ? null : form.destination.trim(),
        requiredSuccessfulVerifications: Number(form.required),
        verificationCadenceDays: Number(form.cadence),
        rowVersion: settings.rowVersion,
      });
      setBaseline(form);
      setConflict(null);
      notify("success", "Backup settings saved.");
      onSaved();
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else if (err instanceof ApiError && err.status === 403) setFormError("You don't have permission to change backup settings.");
      else setFormError(describeBackupError(err));
    } finally {
      setSaving(false);
    }
  }

  const set = (patch: Partial<FormState>) => setForm((prev) => ({ ...prev, ...patch }));

  return (
    <section className="alv-config-panel" aria-label="Backup settings">
      <h2 className="alv-config-panel__form-title">Schedule, retention and verification policy</h2>
      <form className="alv-config-panel__form" onSubmit={submit} noValidate aria-label="Backup settings form">
        {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => { setConflict(null); setForm(baseline); onSaved(); }} />}
        <fieldset className="alv-config-panel__fields" disabled={saving}>
          <label className="alv-config-panel__toggle">
            <input type="checkbox" checked={form.scheduleEnabled} onChange={(e) => set({ scheduleEnabled: e.target.checked })} />
            Run scheduled backups automatically
          </label>
          {errors.scheduleEnabled && (
            <p className="alv-form-field__error" role="alert">
              {errors.scheduleEnabled}
            </p>
          )}
          <FormField label="Hours between scheduled backups" type="number" min={1} max={168} value={form.interval} error={errors.interval} onChange={(e) => set({ interval: e.target.value })} />
          <FormField label="Backups to keep" type="number" min={1} max={365} value={form.retention} error={errors.retention} onChange={(e) => set({ retention: e.target.value })} hint="The newest fully verified backup is always kept." />
          <FormField label="Backup folder" value={form.destination} error={errors.destination} onChange={(e) => set({ destination: e.target.value })} hint={`Leave blank to use the default (${settings.destinationIsDefault ? settings.destinationDirectory : "server default"}).`} />
          <FormField label="Verifications required before scheduled backups are trusted" type="number" min={0} max={10} value={form.required} error={errors.required} onChange={(e) => set({ required: e.target.value })} hint="Full verifications or restore drills with the recovery key." />
          <FormField label="Re-verify at least every (days)" type="number" min={1} max={365} value={form.cadence} error={errors.cadence} onChange={(e) => set({ cadence: e.target.value })} />
        </fieldset>
        {formError && (
          <p className="alv-form-field__error" role="alert">
            {formError}
          </p>
        )}
        <div className="alv-config-panel__form-actions">
          <Button type="submit" variant="primary" disabled={!dirty || saving}>
            {saving ? "Saving…" : "Save settings"}
          </Button>
          {dirty && <span className="alv-config-panel__dirty">Unsaved changes</span>}
        </div>
      </form>
    </section>
  );
}
