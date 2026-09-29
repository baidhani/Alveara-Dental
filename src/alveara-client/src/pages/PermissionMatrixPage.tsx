import { useCallback, useEffect, useState } from "react";
import { PageHeader } from "../components/PageHeader";
import { LoadingState, ErrorState } from "../components/StatePatterns";
import { PermissionDenied } from "../components/PermissionDenied";
import { Button } from "../components/Button";
import { ApiError, getPermissionMatrix } from "../services/authApi";
import type { PermissionMatrixEntry } from "../services/authApi";
import "./PermissionMatrixPage.css";

type PageState =
  | { kind: "loading" }
  | { kind: "permission-denied" }
  | { kind: "error" }
  | { kind: "loaded"; matrix: PermissionMatrixEntry[] };

/** ALV-001-C01's permission-visibility screen (review finding ALV-001-C01-R01-05): the full
 *  role -> permission matrix, read directly from the server's own authorization source of truth
 *  (GET /api/auth/permission-matrix) rather than a document that could drift from it. */
export function PermissionMatrixPage() {
  const [state, setState] = useState<PageState>({ kind: "loading" });

  const load = useCallback(async () => {
    setState({ kind: "loading" });
    try {
      const matrix = await getPermissionMatrix();
      setState({ kind: "loaded", matrix });
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        setState({ kind: "permission-denied" });
      } else {
        setState({ kind: "error" });
      }
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const allPermissions = state.kind === "loaded" ? Array.from(new Set(state.matrix.flatMap((r) => r.permissions))).sort() : [];

  return (
    <>
      <PageHeader title="Permission matrix" description="Which permissions each role holds. This is the server's actual authorization source of truth, not a description of it." />

      {state.kind === "loading" && <LoadingState label="Loading permission matrix…" />}
      {state.kind === "permission-denied" && <PermissionDenied requiredPermission="ViewPermissionMatrix" />}
      {state.kind === "error" && <ErrorState title="Could not load the permission matrix" action={<Button onClick={load}>Retry</Button>} />}

      {state.kind === "loaded" && (
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
    </>
  );
}
