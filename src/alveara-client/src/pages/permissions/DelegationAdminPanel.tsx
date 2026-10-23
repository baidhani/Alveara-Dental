import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { getDelegations, isStaleWrite, newPolicyKey, policyErrorMessage, setDelegations } from "../../services/permissionPolicyApi";
import type { DelegationRow, DelegationTable } from "../../services/permissionPolicyApi";
import "./permissions.css";

const same = (a: DelegationRow, b: DelegationRow) => a.delegateRole === b.delegateRole && a.permission === b.permission && a.targetRole === b.targetRole;

/**
 * ALV-N018: the administrator's delegation rules. Only the office manager can be given a delegation, only for a delegable permission (never a protected permission, account status or session revocation) and only
 * for the five other working roles; the choices below are the server's own lists, and the server refuses anything else. The change needs a written reason and a confirmation.
 */
export function DelegationAdminPanel({ refreshKey, onSaved }: { refreshKey: number; onSaved: (message: string) => void }) {
  const [table, setTable] = useState<DelegationTable | null>(null);
  const [rows, setRows] = useState<DelegationRow[]>([]);
  const [failed, setFailed] = useState(false);
  const [draft, setDraft] = useState({ delegateRole: "", permission: "", targetRole: "" });
  const [reason, setReason] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const key = useRef(newPolicyKey());

  const load = useCallback(async () => {
    try {
      const t = await getDelegations();
      setTable(t); setRows(t.rows); setFailed(false);
      setDraft({ delegateRole: t.delegableDelegates[0] ?? "", permission: t.delegablePermissions[0] ?? "", targetRole: t.targetRoles[0] ?? "" });
    } catch { setFailed(true); }
  }, []);
  useEffect(() => { void load(); }, [load, refreshKey]);

  if (failed) return <p role="status">The delegation rules could not be loaded.</p>;
  if (!table) return null;
  const dirty = rows.length !== table.rows.length || rows.some((r) => !table.rows.some((t) => same(r, t)));
  const edit = (next: DelegationRow[]) => { key.current = newPolicyKey(); setRows(next); };

  async function save(e: FormEvent) {
    e.preventDefault();
    if (!table || busy || !dirty || !confirmed || reason.trim().length === 0) return;
    setBusy(true); setError(null);
    try {
      await setDelegations({ rows, reason: reason.trim(), expectedVersion: table.policyVersion, confirmed }, key.current);
      key.current = newPolicyKey(); setReason(""); setConfirmed(false);
      onSaved("The delegation rules were saved. They apply from the next request.");
      await load();
    } catch (err) {
      setError(policyErrorMessage(err));
      if (isStaleWrite(err)) await load();
    } finally { setBusy(false); }
  }

  return (
    <section aria-labelledby="alv-deleg-admin-title" className="alv-policy-panel">
      <h2 id="alv-deleg-admin-title">Delegation rules</h2>
      <p>Who may grant or revoke which permission for which role. A delegate can only change a permission it holds itself.</p>
      <table className="alv-permission-matrix">
        <caption>Delegation rules</caption>
        <thead><tr><th scope="col">Delegate</th><th scope="col">Permission</th><th scope="col">For role</th><th scope="col">Action</th></tr></thead>
        <tbody>
          {rows.length === 0 && <tr><td colSpan={4}>No delegations: nobody may change another role's permissions.</td></tr>}
          {rows.map((r) => (
            <tr key={`${r.delegateRole}|${r.permission}|${r.targetRole}`}>
              <td>{r.delegateRole}</td><td>{r.permission}</td><td>{r.targetRole}</td>
              <td><Button type="button" onClick={() => edit(rows.filter((x) => !same(x, r)))} aria-label={`Remove the rule: ${r.delegateRole} may change ${r.permission} for ${r.targetRole}`}>Remove</Button></td>
            </tr>
          ))}
        </tbody>
      </table>
      <fieldset className="alv-policy-add">
        <legend>Add a rule</legend>
        <label>Delegate <select value={draft.delegateRole} onChange={(e) => setDraft({ ...draft, delegateRole: e.target.value })}>{table.delegableDelegates.map((r) => <option key={r}>{r}</option>)}</select></label>
        <label>Permission <select value={draft.permission} onChange={(e) => setDraft({ ...draft, permission: e.target.value })}>{table.delegablePermissions.map((p) => <option key={p}>{p}</option>)}</select></label>
        <label>For role <select value={draft.targetRole} onChange={(e) => setDraft({ ...draft, targetRole: e.target.value })}>{table.targetRoles.map((r) => <option key={r}>{r}</option>)}</select></label>
        <Button type="button" onClick={() => { if (!rows.some((x) => same(x, draft))) edit([...rows, { ...draft }]); }}>Add the rule</Button>
      </fieldset>
      {dirty && (
        <form className="alv-policy-editor" onSubmit={save} aria-labelledby="alv-deleg-save-title" noValidate>
          <h3 id="alv-deleg-save-title">Save the delegation rules</h3>
          {error && <p role="alert" className="alv-policy-error">{error}</p>}
          <div className="alv-form-field">
            <label htmlFor="alv-deleg-admin-reason" className="alv-form-field__label">Reason (required, up to 500 characters)</label>
            <textarea id="alv-deleg-admin-reason" rows={2} maxLength={500} value={reason} onChange={(e) => { key.current = newPolicyKey(); setReason(e.target.value); }} />
          </div>
          <label className="alv-policy-check"><input type="checkbox" checked={confirmed} onChange={(e) => { key.current = newPolicyKey(); setConfirmed(e.target.checked); }} /> I confirm this change</label>
          <div className="alv-policy-actions">
            <Button type="submit" variant="primary" disabled={busy || !confirmed || reason.trim().length === 0}>{busy ? "Saving…" : "Save the rules"}</Button>
            <Button type="button" onClick={() => setRows(table.rows)}>Discard</Button>
          </div>
        </form>
      )}
    </section>
  );
}
