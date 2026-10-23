import { Button } from "../../components/Button";
import type { EffectiveCell, EffectivePolicy } from "../../services/permissionPolicyApi";
import "./permissions.css";

export type CellRef = { role: string; permission: string };

const PROTECTED = new Set(["ManageUsers", "ManageRoles", "ManageMfaPolicy", "IssuePasswordResets", "ManagePermissionGrants"]);

/**
 * ALV-N018: the administrator's editable permission matrix, read from the effective view. Each cell says whether the role holds the permission and where that comes from (default, granted or revoked, with who,
 * when and why on the cell's title and in the editor); the protected permissions and the administrator and unassigned columns are shown locked and have no change button; a role whose stored policy needs repair
 * is marked and cannot be edited.
 */
export function EditableMatrix({ policy, selected, onChange }: { policy: EffectivePolicy; selected: CellRef | null; onChange: (cell: CellRef) => void }) {
  const permissions = policy.roles[0]?.permissions.map((c) => c.permission) ?? [];
  const cellOf = (role: string, permission: string): EffectiveCell => policy.roles.find((r) => r.role === role)!.permissions.find((c) => c.permission === permission)!;

  return (
    <div className="alv-permission-matrix-scroll">
      <table className="alv-permission-matrix alv-policy-matrix">
        <caption>Permission matrix: which permissions each role holds and where each setting comes from</caption>
        <thead>
          <tr>
            <th scope="col">Permission</th>
            {policy.roles.map((r) => (
              <th scope="col" key={r.role}>
                {r.role}
                <span className="alv-policy-sub">{r.enabledUsers} enabled</span>
                {r.health !== "Healthy" && <span className="alv-policy-sub alv-policy-error">{r.health === "RepairRequired" ? "needs repair" : "degraded"}</span>}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {permissions.map((permission) => (
            <tr key={permission}>
              <th scope="row">
                {permission}
                {PROTECTED.has(permission) && <span className="alv-policy-sub">locked: Administrator only</span>}
              </th>
              {policy.roles.map((r) => {
                const cell = cellOf(r.role, permission);
                const locked = !cell.grantable;
                const title = cell.source === "default" ? undefined : `${cell.source} by an administrator${cell.reason ? `: ${cell.reason}` : ""}`;
                const isSelected = selected?.role === r.role && selected.permission === permission;
                return (
                  <td key={r.role} className={`alv-permission-matrix__cell${isSelected ? " alv-policy-selected" : ""}`} title={title}>
                    <span>{cell.effective ? "Granted" : "Not granted"}</span>
                    <span className={`alv-policy-source alv-policy-source--${cell.source}`}>{cell.source}</span>
                    {locked ? (
                      <span className="alv-policy-sub">locked</span>
                    ) : (
                      <Button type="button" onClick={() => onChange({ role: r.role, permission })} disabled={r.health !== "Healthy"} aria-label={`Change ${permission} for ${r.role}`}>Change</Button>
                    )}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
