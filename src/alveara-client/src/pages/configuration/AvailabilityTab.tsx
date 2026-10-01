import { useCallback, useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { PermissionDenied } from "../../components/PermissionDenied";
import { useNotifications } from "../../components/Notification";
import { confirmDiscard, useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError } from "../../services/authApi";
import { addBlockedTime, getAvailability, listBlockedTime, listProviders, removeBlockedTime, replaceAvailability } from "../../services/configApi";
import type { BlockedTime, ProviderRecord } from "../../services/configApi";
import { DAY_NAMES, validateWindows } from "./availabilityRules";
import type { WindowRow } from "./availabilityRules";

let nextKey = 1;
const toRows = (windows: { dayOfWeek: number; startLocal: string; endLocal: string }[]): WindowRow[] =>
  windows.map((w) => ({ key: nextKey++, dayOfWeek: String(w.dayOfWeek), startLocal: w.startLocal, endLocal: w.endLocal }));
const snapshot = (rows: WindowRow[]) => JSON.stringify(rows.map(({ dayOfWeek, startLocal, endLocal }) => ({ dayOfWeek, startLocal, endLocal })));

type ProvidersState = { kind: "loading" } | { kind: "denied" } | { kind: "error" } | { kind: "loaded"; providers: ProviderRecord[] };

/**
 * Provider weekly availability (practice-local wall-clock hours) and blocked time. The weekly
 * schedule is edited as a whole and replaced atomically; blocked time is entered in practice-local
 * time and the server converts it (rejecting a daylight-saving gap/overlap rather than guessing).
 */
export function AvailabilityTab({ onDirtyChange }: { onDirtyChange: (dirty: boolean) => void }) {
  const { notify } = useNotifications();
  const [providersState, setProvidersState] = useState<ProvidersState>({ kind: "loading" });
  const [providerId, setProviderId] = useState("");
  const [rows, setRows] = useState<WindowRow[]>([]);
  const [saved, setSaved] = useState("[]");
  const [errors, setErrors] = useState<Record<number, string>>({});
  const [saveError, setSaveError] = useState<string | null>(null);
  const [scheduleLoading, setScheduleLoading] = useState(false);
  const [scheduleFailed, setScheduleFailed] = useState(false);
  const [blocked, setBlocked] = useState<BlockedTime[]>([]);
  const [blockForm, setBlockForm] = useState({ start: "", end: "", reason: "" });
  const [blockError, setBlockError] = useState<string | null>(null);

  const blockDirty = blockForm.start !== "" || blockForm.end !== "" || blockForm.reason !== "";
  const dirty = snapshot(rows) !== saved || blockDirty;
  useUnsavedChangesWarning(dirty);
  useEffect(() => {
    onDirtyChange(dirty);
  }, [dirty, onDirtyChange]);

  const loadProviders = useCallback(async () => {
    setProvidersState({ kind: "loading" });
    try {
      setProvidersState({ kind: "loaded", providers: await listProviders(false) });
    } catch (err) {
      setProvidersState(err instanceof ApiError && err.status === 403 ? { kind: "denied" } : { kind: "error" });
    }
  }, []);

  useEffect(() => {
    loadProviders();
  }, [loadProviders]);

  async function loadSchedule(id: string) {
    setScheduleLoading(true);
    setScheduleFailed(false);
    try {
      const [windows, blocks] = await Promise.all([getAvailability(id), listBlockedTime(id)]);
      const loadedRows = toRows(windows);
      setRows(loadedRows);
      setSaved(snapshot(loadedRows));
      setBlocked(blocks);
    } catch {
      setScheduleFailed(true);
    } finally {
      setScheduleLoading(false);
    }
  }

  function chooseProvider(id: string) {
    if (!confirmDiscard(dirty)) return;
    setProviderId(id);
    setErrors({});
    setSaveError(null);
    setBlockForm({ start: "", end: "", reason: "" });
    setBlockError(null);
    if (id) loadSchedule(id);
    else {
      setRows([]);
      setSaved("[]");
      setBlocked([]);
    }
  }

  function updateRow(key: number, patch: Partial<WindowRow>) {
    setRows((prev) => prev.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  async function saveSchedule() {
    const found = validateWindows(rows);
    setErrors(found);
    setSaveError(null);
    if (Object.keys(found).length > 0) return;
    try {
      const result = await replaceAvailability(
        providerId,
        rows.map((r) => ({ dayOfWeek: Number(r.dayOfWeek), startLocal: r.startLocal, endLocal: r.endLocal }))
      );
      const next = toRows(result);
      setRows(next);
      setSaved(snapshot(next));
      notify("success", "Weekly availability saved.");
    } catch (err) {
      setSaveError(err instanceof ApiError ? err.message : "Could not save availability. Check your connection and try again.");
    }
  }

  async function submitBlock(event: FormEvent) {
    event.preventDefault();
    setBlockError(null);
    if (!blockForm.start || !blockForm.end) {
      setBlockError("Enter both a start and an end.");
      return;
    }
    if (blockForm.end <= blockForm.start) {
      setBlockError("Blocked time must end after it starts.");
      return;
    }
    try {
      await addBlockedTime(providerId, blockForm.start, blockForm.end, blockForm.reason.trim());
      setBlockForm({ start: "", end: "", reason: "" });
      setBlocked(await listBlockedTime(providerId));
      notify("success", "Blocked time added.");
    } catch (err) {
      setBlockError(err instanceof ApiError ? err.message : "Could not add blocked time.");
    }
  }

  async function removeBlock(id: string) {
    try {
      await removeBlockedTime(providerId, id);
      setBlocked(await listBlockedTime(providerId));
      notify("success", "Blocked time removed.");
    } catch (err) {
      notify("danger", err instanceof ApiError ? err.message : "Could not remove blocked time.");
    }
  }

  if (providersState.kind === "loading") return <LoadingState label="Loading providers…" />;
  if (providersState.kind === "denied") return <PermissionDenied requiredPermission="ManagePracticeConfiguration" />;
  if (providersState.kind === "error") {
    return <ErrorState title="Could not load providers" action={<Button onClick={loadProviders}>Retry</Button>} />;
  }
  if (providersState.providers.length === 0) {
    return <EmptyState title="No active providers yet" description="Add a provider on the Providers tab, then set their working hours here." />;
  }

  return (
    <section className="alv-config-panel" aria-label="Availability">
      <div className="alv-form-field">
        <label className="alv-form-field__label" htmlFor="availability-provider">
          Provider
        </label>
        <select id="availability-provider" className="alv-form-field__input" value={providerId} onChange={(e) => chooseProvider(e.target.value)}>
          <option value="">Select a provider…</option>
          {providersState.providers.map((p) => (
            <option key={p.id} value={p.id}>
              {p.displayName} - {p.specialty}
            </option>
          ))}
        </select>
      </div>

      {providerId && scheduleLoading && <LoadingState label="Loading schedule…" />}
      {providerId && scheduleFailed && <ErrorState title="Could not load this provider's schedule" action={<Button onClick={() => loadSchedule(providerId)}>Retry</Button>} />}

      {providerId && !scheduleLoading && !scheduleFailed && (
        <>
          <h2 className="alv-config-panel__form-title">Weekly hours</h2>
          <p className="alv-config-panel__note">Times are practice-local wall-clock hours; they mean the same hours before and after a daylight-saving change.</p>
          {rows.length === 0 && <EmptyState title="No weekly hours yet" description="Add a window to make this provider schedulable." />}
          {rows.map((row, i) => (
            <div key={row.key} className="alv-config-panel__window" role="group" aria-label={`Window ${i + 1}`}>
              <label>
                <span className="alv-visually-hidden">Day for window {i + 1}</span>
                <select value={row.dayOfWeek} onChange={(e) => updateRow(row.key, { dayOfWeek: e.target.value })}>
                  {DAY_NAMES.map((name, d) => (
                    <option key={name} value={d}>
                      {name}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                <span className="alv-visually-hidden">Start time for window {i + 1}</span>
                <input type="time" value={row.startLocal} onChange={(e) => updateRow(row.key, { startLocal: e.target.value })} />
              </label>
              <label>
                <span className="alv-visually-hidden">End time for window {i + 1}</span>
                <input type="time" value={row.endLocal} onChange={(e) => updateRow(row.key, { endLocal: e.target.value })} />
              </label>
              <Button type="button" onClick={() => setRows((prev) => prev.filter((r) => r.key !== row.key))} aria-label={`Remove window ${i + 1}`}>
                Remove
              </Button>
              {errors[i] && (
                <p className="alv-form-field__error" role="alert">
                  {errors[i]}
                </p>
              )}
            </div>
          ))}
          {saveError && (
            <p className="alv-form-field__error" role="alert">
              {saveError}
            </p>
          )}
          <div className="alv-config-panel__form-actions">
            <Button type="button" onClick={() => setRows((prev) => [...prev, { key: nextKey++, dayOfWeek: "1", startLocal: "09:00", endLocal: "17:00" }])}>
              Add window
            </Button>
            <Button type="button" variant="primary" onClick={saveSchedule} disabled={snapshot(rows) === saved}>
              Save weekly hours
            </Button>
            {snapshot(rows) !== saved && <span className="alv-config-panel__dirty">Unsaved changes</span>}
          </div>

          <h2 className="alv-config-panel__form-title">Blocked time</h2>
          {blocked.length === 0 && <EmptyState title="No blocked time" description="Add time off, meetings, or other periods this provider cannot be booked." />}
          {blocked.length > 0 && (
            <table className="alv-config-panel__table">
              <thead>
                <tr>
                  <th>From (practice time)</th>
                  <th>To (practice time)</th>
                  <th>Reason</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {blocked.map((b) => (
                  <tr key={b.id}>
                    <td>{b.startLocal.replace("T", " ")}</td>
                    <td>{b.endLocal.replace("T", " ")}</td>
                    <td>{b.reason ?? "-"}</td>
                    <td>
                      <Button onClick={() => removeBlock(b.id)} aria-label={`Remove blocked time starting ${b.startLocal.replace("T", " ")}`}>
                        Remove
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <form className="alv-config-panel__form" onSubmit={submitBlock} noValidate aria-label="Add blocked time">
            <FormField label="Blocked from" type="datetime-local" value={blockForm.start} onChange={(e) => setBlockForm({ ...blockForm, start: e.target.value })} />
            <FormField label="Blocked until" type="datetime-local" value={blockForm.end} onChange={(e) => setBlockForm({ ...blockForm, end: e.target.value })} />
            <FormField label="Reason (optional)" type="text" maxLength={200} value={blockForm.reason} onChange={(e) => setBlockForm({ ...blockForm, reason: e.target.value })} />
            {blockError && (
              <p className="alv-form-field__error" role="alert">
                {blockError}
              </p>
            )}
            <div className="alv-config-panel__form-actions">
              <Button type="submit" variant="primary">
                Add blocked time
              </Button>
            </div>
          </form>
        </>
      )}
    </section>
  );
}
