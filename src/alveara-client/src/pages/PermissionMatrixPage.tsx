import { useCallback, useEffect, useRef, useState } from "react";
import { PageHeader } from "../components/PageHeader";
import { LoadingState, ErrorState } from "../components/StatePatterns";
import { PermissionDenied } from "../components/PermissionDenied";
import { Button } from "../components/Button";
import { useAuth } from "../contexts/AuthContext";
import { ApiError, getPermissionMatrix } from "../services/authApi";
import type { PermissionMatrixEntry } from "../services/authApi";
import { getEffective } from "../services/permissionPolicyApi";
import type { EffectivePolicy } from "../services/permissionPolicyApi";
import { CellEditor } from "./permissions/CellEditor";
import { DelegationAdminPanel } from "./permissions/DelegationAdminPanel";
import { DocumentImportCard } from "./permissions/DocumentImportCard";
import { EditableMatrix } from "./permissions/EditableMatrix";
import type { CellRef } from "./permissions/EditableMatrix";
import { PolicyHealthPanel } from "./permissions/PolicyHealthPanel";
import { PolicyHistoryPanel } from "./permissions/PolicyHistoryPanel";
import "./PermissionMatrixPage.css";
import "./permissions/permissions.css";

type PageState =
  | { kind: "loading" }
  | { kind: "permission-denied" }
  | { kind: "error" }
  | { kind: "loaded"; matrix: PermissionMatrixEntry[]; policy: EffectivePolicy | null };

/** The matrix is re-read this often. Item 44 requires a change made elsewhere to show within 30 seconds, so the interval keeps a margin of 5 seconds for the request and the render (ALV-N018-R01-01). Not refreshed while an editor is open or the page is hidden. */
export const MATRIX_REFRESH_MS = 25_000;

/**
 * ALV-001-C01's permission-visibility screen: the full role-to-permission matrix, read from the server's own authorization source of truth. ALV-N018: for a holder of the permission-grant permission (an administrator)
 * it is the editable matrix (source of every cell, reason, confirmation, revert, stale-write recovery, locked protected permissions), with the policy health and repair panel and the delegation rules; for anyone who
 * holds a delegation (the office manager) it also shows the "Who may import documents" card; holders of the audit-log permission see the change history. Everyone else with the view permission sees the read-only matrix.
 */
export function PermissionMatrixPage() {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission("ManagePermissionGrants");
  const [state, setState] = useState<PageState>({ kind: "loading" });
  const [selected, setSelected] = useState<CellRef | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const editing = useRef(false);
  editing.current = selected !== null;

  const load = useCallback(async (quiet = false) => {
    if (!quiet) setState({ kind: "loading" });
    try {
      const [matrix, policy] = await Promise.all([getPermissionMatrix(), canEdit ? getEffective() : Promise.resolve(null)]);
      setState({ kind: "loaded", matrix, policy });
    } catch (err) {
      if (quiet) return;                                                                                   // a failed background refresh leaves what is on screen
      setState(err instanceof ApiError && err.status === 403 ? { kind: "permission-denied" } : { kind: "error" });
    }
  }, [canEdit]);

  useEffect(() => { void load(); }, [load]);
  useEffect(() => {
    const refresh = () => { if (!editing.current && document.visibilityState !== "hidden") { void load(true); setRefreshKey((k) => k + 1); } };
    const timer = window.setInterval(refresh, MATRIX_REFRESH_MS);
    window.addEventListener("focus", refresh);
    return () => { window.clearInterval(timer); window.removeEventListener("focus", refresh); };
  }, [load]);

  const changed = (text: string) => { setMessage(text); setSelected(null); setRefreshKey((k) => k + 1); void load(true); };
  const allPermissions = state.kind === "loaded" ? Array.from(new Set(state.matrix.flatMap((r) => r.permissions))).sort() : [];
  const selectedRole = state.kind === "loaded" && state.policy && selected ? state.policy.roles.find((r) => r.role === selected.role) : undefined;
  const selectedCell = selectedRole?.permissions.find((c) => c.permission === selected?.permission);

  return (
    <>
      <PageHeader title="Permission matrix" description="Which permissions each role holds. This is the server's actual authorization source of truth, not a description of it." />

      {state.kind === "loading" && <LoadingState label="Loading permission matrix…" />}
      {state.kind === "permission-denied" && <PermissionDenied requiredPermission="ViewPermissionMatrix" />}
      {state.kind === "error" && <ErrorState title="Could not load the permission matrix" action={<Button onClick={() => load()}>Retry</Button>} />}

      {state.kind === "loaded" && (
        <>
          {message && <p role="status">{message}</p>}
          <DocumentImportCard onChanged={() => void load(true)} />
          {state.policy ? (
            <>
              <p className="alv-policy-polling">Changes made by an administrator apply from the next request and show here within about {MATRIX_REFRESH_MS / 1000} seconds. Policy version {state.policy.version}.</p>
              <EditableMatrix policy={state.policy} selected={selected} onChange={(c) => { setSelected(c); setMessage(null); }} />
              {selected && selectedRole && selectedCell && (
                <CellEditor key={`${selected.role}|${selected.permission}|${state.policy.version}`} role={selectedRole} cell={selectedCell} version={state.policy.version}
                  onSaved={changed} onCancel={() => setSelected(null)} onStale={() => { setSelected(null); void load(); }} />
              )}
              <PolicyHealthPanel health={state.policy.health} onRepaired={changed} />
              <DelegationAdminPanel refreshKey={refreshKey} onSaved={changed} />
            </>
          ) : (
            <div className="alv-permission-matrix-scroll">
              <table className="alv-permission-matrix">
                <thead>
                  <tr>
                    <th>Permission</th>
                    {state.matrix.map((r) => (
                      <th key={r.role}>{r.role}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {allPermissions.map((permission) => (
                    <tr key={permission}>
                      <td>{permission}</td>
                      {state.matrix.map((r) => (
                        <td key={r.role} className="alv-permission-matrix__cell">
                          {r.permissions.includes(permission) ? (
                            <span aria-label="granted">✓</span>
                          ) : (
                            <span aria-label="not granted" className="alv-permission-matrix__no">
                              —
                            </span>
                          )}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {hasPermission("ViewAuditLog") && <PolicyHistoryPanel refreshKey={refreshKey} />}
        </>
      )}
    </>
  );
}
