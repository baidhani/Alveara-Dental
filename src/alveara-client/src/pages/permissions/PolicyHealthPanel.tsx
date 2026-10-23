import { useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { policyErrorMessage, removeInvalidRow } from "../../services/permissionPolicyApi";
import type { InvalidRow, PolicyHealth } from "../../services/permissionPolicyApi";
import "./permissions.css";

/**
 * ALV-N018: the administrator's policy health and repair panel. A healthy policy says so. Each stored row the engine refused (invalid persisted data, which never restores access) is listed with its raw values and
 * why, and can be removed with a written reason; the role it names stays denied until then. Nothing here edits a valid row.
 */
export function PolicyHealthPanel({ health, onRepaired }: { health: PolicyHealth; onRepaired: (message: string) => void }) {
  const [repairing, setRepairing] = useState<InvalidRow | null>(null);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function remove(e: FormEvent) {
    e.preventDefault();
    if (!repairing || busy || reason.trim().length === 0) return;
    setBusy(true); setError(null);
    try {
      await removeInvalidRow(repairing.id, reason.trim(), repairing.rowVersion);
      setRepairing(null); setReason("");
      onRepaired("The invalid row was removed. The policy was re-read.");
    } catch (err) { setError(policyErrorMessage(err)); } finally { setBusy(false); }
  }

  return (
    <section aria-labelledby="alv-policy-health-title" className="alv-policy-panel">
      <h2 id="alv-policy-health-title">Policy health</h2>
      {health.status === "Healthy" ? (
        <p role="status">The stored permission policy is healthy: {health.overrideCount} change{health.overrideCount === 1 ? "" : "s"} from the defaults, no invalid rows.</p>
      ) : (
        <p role="alert" className="alv-policy-error">
          Permission policy needs repair: {health.invalidRowCount} invalid row{health.invalidRowCount === 1 ? "" : "s"}.
          {health.status === "Degraded" ? " A row could not be attributed to any role, so every role except Administrator is denied." : health.rolesNeedingRepair.length > 0 ? ` Denied until repaired: ${health.rolesNeedingRepair.join(", ")}.` : ""}
        </p>
      )}
      {health.stale && <p role="status">The policy store could not be read just now; this is the last verified copy.</p>}
      {health.invalidRows.length > 0 && (
        <table className="alv-permission-matrix">
          <caption>Invalid stored rows</caption>
          <thead><tr><th scope="col">Where</th><th scope="col">Role</th><th scope="col">Permission</th><th scope="col">Effect</th><th scope="col">Why it is invalid</th><th scope="col">Action</th></tr></thead>
          <tbody>
            {health.invalidRows.map((r) => (
              <tr key={r.id}>
                <td>{r.table}</td><td>{r.role || "(blank)"}</td><td>{r.permission || "(blank)"}</td><td>{r.effect ?? r.targetRole ?? ""}</td><td>{r.reason}</td>
                <td><Button type="button" onClick={() => { setRepairing(r); setError(null); }} aria-label={`Remove the invalid row for ${r.role} ${r.permission}`}>Remove</Button></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {repairing && (
        <form onSubmit={remove} className="alv-policy-editor" aria-labelledby="alv-policy-remove-title" noValidate>
          <h3 id="alv-policy-remove-title">Remove the invalid row for {repairing.role} {repairing.permission}</h3>
          {error && <p role="alert" className="alv-policy-error">{error}</p>}
          <div className="alv-form-field">
            <label htmlFor="alv-policy-remove-reason" className="alv-form-field__label">Reason (required, up to 500 characters)</label>
            <textarea id="alv-policy-remove-reason" rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
          </div>
          <div className="alv-policy-actions">
            <Button type="submit" variant="danger" disabled={busy || reason.trim().length === 0}>{busy ? "Removing…" : "Remove the row"}</Button>
            <Button type="button" onClick={() => setRepairing(null)}>Cancel</Button>
          </div>
        </form>
      )}
    </section>
  );
}
