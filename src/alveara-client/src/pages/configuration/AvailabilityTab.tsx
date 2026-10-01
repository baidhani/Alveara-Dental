import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { FormField } from "../../components/FormField";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { PermissionDenied } from "../../components/PermissionDenied";
import { useNotifications } from "../../components/Notification";
import { confirmDiscard, useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
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
 * schedule is edited as a whole and replaced atomically, against the revision that was read; blocked
 * time is entered in practice-local time and the server converts it (rejecting a daylight-saving
 * gap/overlap rather than guessing).
 *
 * ALV-N003 R02/R03: asynchronous completion protection is bound to an EDITING SESSION, not to a
 * provider id. A session starts every time an editor is (re)populated - choosing a provider and every
 * successful (re)load - and `sessionRef` advances at each of those moments. Every request captures the
 * session it was issued in and its completion (success, error, notification, follow-up refresh) is
 * discarded unless that session is still current. Provider identity alone cannot do this: switching
 * A -> B -> A makes an abandoned session's response look current again. Within a session, a save also
 * must not clobber edits typed after the snapshot it submitted: a draft counter is captured at submit
 * and a changed draft keeps the user's newer rows (only the saved baseline and revision advance).
 */
export function AvailabilityTab({ onDirtyChange }: { onDirtyChange: (dirty: boolean) => void }) {
  const { notify } = useNotifications();
  const [providersState, setProvidersState] = useState<ProvidersState>({ kind: "loading" });
  const [providerId, setProviderId] = useState("");
  /** The provider whose schedule is actually in `rows`/`revision`/`blocked`; "" while nothing valid is loaded. */
  const [loadedFor, setLoadedFor] = useState("");
  const [rows, setRows] = useState<WindowRow[]>([]);
  const [saved, setSaved] = useState("[]");
  const [revision, setRevision] = useState(0);
  const [errors, setErrors] = useState<Record<number, string>>({});
  const [saveError, setSaveError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const [reloadError, setReloadError] = useState<string | null>(null);
  const [scheduleLoading, setScheduleLoading] = useState(false);
  const [scheduleFailed, setScheduleFailed] = useState(false);
  const [blocked, setBlocked] = useState<BlockedTime[]>([]);
  const [blockForm, setBlockForm] = useState({ start: "", end: "", reason: "" });
  const [blockError, setBlockError] = useState<string | null>(null);

  /** Advances whenever an editor is (re)populated; every async completion must match the session it started in. */
  const sessionRef = useRef(0);
  /** Request identity for loads, independent of provider identity. */
  const loadSeq = useRef(0);
  /** Incremented on every schedule edit / blocked-time form edit, to detect input typed during an in-flight write. */
  const draftRef = useRef(0);
  const blockDraftRef = useRef(0);
  const [saving, setSaving] = useState(false);

  const blockDirty = blockForm.start !== "" || blockForm.end !== "" || blockForm.reason !== "";
  const scheduleDirty = loadedFor !== "" && snapshot(rows) !== saved;
  const dirty = scheduleDirty || blockDirty;
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

  /**
   * Loads one provider's schedule. `keepDraftOnFailure` is for conflict recovery, where the user's
   * draft is still on screen: only a SUCCESSFUL reload may replace it, a failed one leaves it intact.
   */
  async function loadSchedule(id: string, keepDraftOnFailure = false) {
    const seq = ++loadSeq.current;
    if (!keepDraftOnFailure) {
      setScheduleLoading(true);
      setScheduleFailed(false);
    }
    try {
      const [schedule, blocks] = await Promise.all([getAvailability(id), listBlockedTime(id)]);
      if (seq !== loadSeq.current) return; // superseded by a newer selection/load: discard
      sessionRef.current += 1;             // a freshly populated editor is a new editing session
      draftRef.current += 1;
      blockDraftRef.current += 1;
      setSaving(false);
      const loadedRows = toRows(schedule.windows);
      setRows(loadedRows);
      setSaved(snapshot(loadedRows));
      setRevision(schedule.revision);
      setBlocked(blocks);
      setLoadedFor(id);
      setConflict(null);
      setReloadError(null);
      setSaveError(null);
      setErrors({});
    } catch (err) {
      if (seq !== loadSeq.current) return;
      if (keepDraftOnFailure) {
        setReloadError(err instanceof ApiError ? err.message : "Could not reload the current schedule. Check your connection and try again.");
      } else {
        setScheduleFailed(true);
      }
    } finally {
      if (seq === loadSeq.current) setScheduleLoading(false);
    }
  }

  function chooseProvider(id: string) {
    if (!confirmDiscard(dirty)) return;
    loadSeq.current += 1;    // invalidate any load still in flight
    sessionRef.current += 1; // abandon the current editing session: its write completions must now be ignored
    draftRef.current += 1;
    blockDraftRef.current += 1;
    setSaving(false);
    setProviderId(id);
    // Clear everything bound to the previous provider immediately, so it can never be shown or saved under the new one.
    setLoadedFor("");
    setRows([]);
    setSaved("[]");
    setRevision(0);
    setBlocked([]);
    setErrors({});
    setSaveError(null);
    setConflict(null);
    setReloadError(null);
    setScheduleFailed(false);
    setScheduleLoading(false);
    setBlockForm({ start: "", end: "", reason: "" });
    setBlockError(null);
    if (id) loadSchedule(id);
  }

  function updateRow(key: number, patch: Partial<WindowRow>) {
    draftRef.current += 1;
    setRows((prev) => prev.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  function addWindow() {
    draftRef.current += 1;
    setRows((prev) => [...prev, { key: nextKey++, dayOfWeek: "1", startLocal: "09:00", endLocal: "17:00" }]);
  }

  function removeWindow(key: number) {
    draftRef.current += 1;
    setRows((prev) => prev.filter((r) => r.key !== key));
  }

  function editBlockForm(patch: Partial<{ start: string; end: string; reason: string }>) {
    blockDraftRef.current += 1;
    setBlockForm((prev) => ({ ...prev, ...patch }));
  }

  async function saveSchedule() {
    const targetId = loadedFor; // the provider these rows were loaded for
    if (targetId === "" || saving) return;
    const found = validateWindows(rows);
    setErrors(found);
    setSaveError(null);
    if (Object.keys(found).length > 0) return;

    const session = sessionRef.current;     // the editing session this write belongs to
    const draftAtSubmit = draftRef.current; // what the user had typed when they submitted
    setSaving(true);
    try {
      const result = await replaceAvailability(
        targetId,
        rows.map((r) => ({ dayOfWeek: Number(r.dayOfWeek), startLocal: r.startLocal, endLocal: r.endLocal })),
        revision
      );
      if (session !== sessionRef.current) return; // abandoned session (even if the same provider is open again): ignore
      setRevision(result.revision);
      setConflict(null);
      const savedRows = toRows(result.windows);
      if (draftRef.current === draftAtSubmit) {
        setRows(savedRows);
      }
      // else: the user kept typing while the save was in flight - keep their newer rows, which now differ
      // from the new saved baseline and so remain dirty and saveable at the new revision.
      setSaved(snapshot(savedRows));
      notify("success", "Weekly availability saved.");
    } catch (err) {
      if (session !== sessionRef.current) return;
      if (isConcurrencyConflict(err)) {
        setConflict(err.body);
        setReloadError(null);
      } else {
        setSaveError(err instanceof ApiError ? err.message : "Could not save availability. Check your connection and try again.");
      }
    } finally {
      if (session === sessionRef.current) setSaving(false);
    }
  }

  async function submitBlock(event: FormEvent) {
    event.preventDefault();
    const targetId = loadedFor;
    if (targetId === "") return;
    setBlockError(null);
    if (!blockForm.start || !blockForm.end) {
      setBlockError("Enter both a start and an end.");
      return;
    }
    if (blockForm.end <= blockForm.start) {
      setBlockError("Blocked time must end after it starts.");
      return;
    }
    const session = sessionRef.current;
    const blockDraftAtSubmit = blockDraftRef.current;
    try {
      await addBlockedTime(targetId, blockForm.start, blockForm.end, blockForm.reason.trim());
      const blocks = await listBlockedTime(targetId);
      if (session !== sessionRef.current) return;
      if (blockDraftRef.current === blockDraftAtSubmit) setBlockForm({ start: "", end: "", reason: "" }); // never wipe newer typing
      setBlocked(blocks);
      notify("success", "Blocked time added.");
    } catch (err) {
      if (session !== sessionRef.current) return;
      setBlockError(err instanceof ApiError ? err.message : "Could not add blocked time.");
    }
  }

  async function removeBlock(id: string) {
    const targetId = loadedFor;
    if (targetId === "") return;
    const session = sessionRef.current;
    try {
      await removeBlockedTime(targetId, id);
      const blocks = await listBlockedTime(targetId);
      if (session !== sessionRef.current) return;
      setBlocked(blocks);
      notify("success", "Blocked time removed.");
    } catch (err) {
      if (session !== sessionRef.current) return;
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

  const editorReady = providerId !== "" && loadedFor === providerId && !scheduleLoading && !scheduleFailed;

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

      {editorReady && (
        <>
          <h2 className="alv-config-panel__form-title">Weekly hours</h2>
          <p className="alv-config-panel__note">Times are practice-local wall-clock hours; they mean the same hours before and after a daylight-saving change.</p>
          {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => loadSchedule(providerId, true)} />}
          {reloadError && (
            <p className="alv-form-field__error" role="alert">
              {reloadError} Your edits are still here - use "Reload current version" to try again.
            </p>
          )}
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
              <Button type="button" onClick={() => removeWindow(row.key)} aria-label={`Remove window ${i + 1}`}>
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
            <Button type="button" onClick={addWindow}>
              Add window
            </Button>
            <Button type="button" variant="primary" onClick={saveSchedule} disabled={!scheduleDirty || saving}>
              {saving ? "Saving…" : "Save weekly hours"}
            </Button>
            {scheduleDirty && <span className="alv-config-panel__dirty">Unsaved changes</span>}
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
            <FormField label="Blocked from" type="datetime-local" value={blockForm.start} onChange={(e) => editBlockForm({ start: e.target.value })} />
            <FormField label="Blocked until" type="datetime-local" value={blockForm.end} onChange={(e) => editBlockForm({ end: e.target.value })} />
            <FormField label="Reason (optional)" type="text" maxLength={200} value={blockForm.reason} onChange={(e) => editBlockForm({ reason: e.target.value })} />
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
