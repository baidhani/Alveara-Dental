import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { delegatedGrant, getMyDelegations, isStaleWrite, newPolicyKey, policyErrorMessage } from "../../services/permissionPolicyApi";
import type { MyDelegations } from "../../services/permissionPolicyApi";
import "./permissions.css";

const TITLES: Record<string, string> = { ImportDocuments: "Who may import documents" };

/**
 * ALV-N018: the delegate's card (today the office manager's "Who may import documents"). It shows, for each delegated permission, the roles the caller may change and whether each currently holds it, with how many
 * enabled accounts a change affects. A change needs a written reason and a confirmation and takes effect on the next request; the server decides what the caller may touch (a role outside the delegation, or a permission the
 * caller does not hold, is refused), so a card that shows nothing means the caller has no delegation. Renders nothing for a caller without one.
 */
export function DocumentImportCard({ onChanged }: { onChanged?: () => void }) {
  const [data, setData] = useState<MyDelegations | null>(null);
  const [failed, setFailed] = useState(false);
  const [pending, setPending] = useState<{ permission: string; role: string; allowed: boolean; users: number } | null>(null);
  const [reason, setReason] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const key = useRef(newPolicyKey());

  const load = useCallback(async () => {
    try { setData(await getMyDelegations()); setFailed(false); } catch { setFailed(true); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!pending || !data || busy || !confirmed || reason.trim().length === 0) return;
    setBusy(true); setError(null);
    try {
      await delegatedGrant({ targetRole: pending.role, permission: pending.permission, allowed: pending.allowed, reason: reason.trim(), expectedVersion: data.policyVersion, confirmed }, key.current);
      key.current = newPolicyKey();
      setMessage(`${pending.role} ${pending.allowed ? "may now" : "may no longer"} ${pending.permission === "ImportDocuments" ? "import documents" : `hold ${pending.permission}`}. It applies to the next request.`);
      setPending(null); setReason(""); setConfirmed(false);
      await load(); onChanged?.();
    } catch (err) {
      setError(policyErrorMessage(err));
      if (isStaleWrite(err)) await load();
    } finally { setBusy(false); }
  }

  if (failed) return <p role="status">The delegation card could not be loaded.</p>;
  if (!data || data.delegations.length === 0) return null;

  return (
    <>
      {data.delegations.map((group) => (
        <section key={group.permission} aria-labelledby={`alv-deleg-${group.permission}`} className="alv-policy-panel">
          <h2 id={`alv-deleg-${group.permission}`}>{TITLES[group.permission] ?? `Who may hold ${group.permission}`}</h2>
          <p>You can allow or stop this for the roles below. Each change is recorded with your name and reason and applies from the next request.</p>
          {message && <p role="status">{message}</p>}
          <table className="alv-permission-matrix">
            <caption>{TITLES[group.permission] ?? group.permission}</caption>
            <thead><tr><th scope="col">Role</th><th scope="col">Enabled accounts</th><th scope="col">Allowed now</th><th scope="col">Change</th></tr></thead>
            <tbody>
              {group.targets.map((t) => (
                <tr key={t.role}>
                  <th scope="row">{t.role}</th>
                  <td>{t.enabledUsers}</td>
                  <td>{t.allowed ? "Yes" : "No"}</td>
                  <td>
                    <Button type="button" onClick={() => { setPending({ permission: group.permission, role: t.role, allowed: !t.allowed, users: t.enabledUsers }); setError(null); setMessage(null); key.current = newPolicyKey(); }}
                      aria-label={`${t.allowed ? "Stop" : "Allow"} ${t.role} ${group.permission === "ImportDocuments" ? "importing documents" : group.permission}`}>{t.allowed ? "Stop" : "Allow"}</Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {pending && pending.permission === group.permission && (
            <form className="alv-policy-editor" onSubmit={submit} aria-labelledby="alv-deleg-form-title" noValidate>
              <h3 id="alv-deleg-form-title">{pending.allowed ? "Allow" : "Stop"} {pending.role}: {pending.permission}</h3>
              <p>This changes access for <strong>{pending.users}</strong> enabled account{pending.users === 1 ? "" : "s"}.</p>
              {error && <p role="alert" className="alv-policy-error">{error}</p>}
              <div className="alv-form-field">
                <label htmlFor="alv-deleg-reason" className="alv-form-field__label">Reason (required, up to 500 characters)</label>
                <textarea id="alv-deleg-reason" rows={2} maxLength={500} value={reason} onChange={(e) => { key.current = newPolicyKey(); setReason(e.target.value); }} />
              </div>
              <label className="alv-policy-check"><input type="checkbox" checked={confirmed} onChange={(e) => { key.current = newPolicyKey(); setConfirmed(e.target.checked); }} /> I confirm this change</label>
              <div className="alv-policy-actions">
                <Button type="submit" variant="primary" disabled={busy || !confirmed || reason.trim().length === 0}>{busy ? "Saving…" : "Save"}</Button>
                <Button type="button" onClick={() => setPending(null)}>Cancel</Button>
              </div>
            </form>
          )}
        </section>
      ))}
    </>
  );
}
