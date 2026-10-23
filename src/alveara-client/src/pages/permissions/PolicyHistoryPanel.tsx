import { useEffect, useState } from "react";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { getPolicyHistory } from "../../services/permissionPolicyApi";
import type { HistoryPage } from "../../services/permissionPolicyApi";
import "./permissions.css";

/** ALV-N018: the immutable permission-change history, for holders of the audit-log permission. `refreshKey` changes when the policy changes so the list follows it. */
export function PolicyHistoryPanel({ refreshKey }: { refreshKey: number }) {
  const [state, setState] = useState<{ kind: "loading" } | { kind: "error" } | { kind: "loaded"; page: HistoryPage }>({ kind: "loading" });

  useEffect(() => {
    let live = true;
    getPolicyHistory().then((page) => { if (live) setState({ kind: "loaded", page }); }).catch(() => { if (live) setState({ kind: "error" }); });
    return () => { live = false; };
  }, [refreshKey]);

  return (
    <section aria-labelledby="alv-policy-history-title" className="alv-policy-panel">
      <h2 id="alv-policy-history-title">Change history</h2>
      {state.kind === "loading" && <LoadingState label="Loading history…" />}
      {state.kind === "error" && <ErrorState title="Could not load the change history" />}
      {state.kind === "loaded" && state.page.items.length === 0 && <EmptyState title="No policy changes yet" description="Changes to who holds which permission are listed here, newest first." />}
      {state.kind === "loaded" && state.page.items.length > 0 && (
        <table className="alv-permission-matrix">
          <caption>{state.page.total} change{state.page.total === 1 ? "" : "s"}, newest first</caption>
          <thead><tr><th scope="col">When</th><th scope="col">Change</th><th scope="col">Role</th><th scope="col">Permission</th><th scope="col">Was</th><th scope="col">Now</th><th scope="col">Reason</th><th scope="col">By</th></tr></thead>
          <tbody>
            {state.page.items.map((h) => (
              <tr key={h.id}>
                <td>{new Date(h.changedAtUtc).toLocaleString()}</td>
                <td>{h.kind}{h.viaDelegation ? " (delegated)" : ""}</td>
                <td>{h.role}{h.targetRole ? ` to ${h.targetRole}` : ""}</td>
                <td>{h.permission}</td>
                <td>{h.oldEffective ? "granted" : "not granted"}</td>
                <td>{h.newEffective ? "granted" : "not granted"}</td>
                <td>{h.reason}</td>
                <td>{h.changedByUserId.slice(0, 8)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
