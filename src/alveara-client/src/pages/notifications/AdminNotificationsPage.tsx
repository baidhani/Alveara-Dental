import { useCallback, useEffect, useState } from "react";
import type { FormEvent } from "react";
import { PageHeader } from "../../components/PageHeader";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { LoadingState, EmptyState, ErrorState } from "../../components/StatePatterns";
import { PermissionDenied } from "../../components/PermissionDenied";
import { useAuth } from "../../contexts/AuthContext";
import { ApiError } from "../../services/authApi";
import { adminHistory, breakGlass } from "../../services/notificationsApi";
import type { BreakGlassResult, HistoryItem, Paged } from "../../services/notificationsApi";

const when = (iso: string | null) => (iso ? new Date(iso).toLocaleString() : "");

/**
 * ALV-N015: the administrator's notification history (metadata only: who, what kind, when, read or hidden - never a subject, a body or a patient) and, for
 * those who hold the permission, the break-glass action that opens ONE message or ONE thread with a written reason and a confirmation. Every attempt is audited.
 */
export function AdminNotificationsPage() {
  const { hasPermission } = useAuth();
  const [status, setStatus] = useState("");
  const [state, setState] = useState<{ kind: "loading" } | { kind: "denied" } | { kind: "error" } | { kind: "loaded"; data: Paged<HistoryItem> }>({ kind: "loading" });

  const load = useCallback(async () => {
    setState({ kind: "loading" });
    try { setState({ kind: "loaded", data: await adminHistory(status) }); }
    catch (err) { setState(err instanceof ApiError && err.status === 403 ? { kind: "denied" } : { kind: "error" }); }
  }, [status]);
  useEffect(() => { void load(); }, [load]);

  return (
    <>
      <PageHeader title="Notification history" description="Who was notified, of what kind, and whether it was read or hidden. This view never shows message text or patients." />
      <label>Status{" "}
        <select value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">All</option><option>Unread</option><option>Read</option><option>Hidden</option>
        </select>
      </label>
      {state.kind === "loading" && <LoadingState label="Loading history…" />}
      {state.kind === "denied" && <PermissionDenied requiredPermission="ViewNotificationAuditHistory" />}
      {state.kind === "error" && <ErrorState title="Could not load the history" action={<Button onClick={load}>Retry</Button>} />}
      {state.kind === "loaded" && state.data.items.length === 0 && <EmptyState title="No notifications" />}
      {state.kind === "loaded" && state.data.items.length > 0 && (
        <table>
          <caption>{state.data.total} notifications</caption>
          <thead><tr><th scope="col">Recipient</th><th scope="col">Kind</th><th scope="col">Priority</th><th scope="col">Status</th><th scope="col">Created</th><th scope="col">Read</th><th scope="col">Hidden</th></tr></thead>
          <tbody>
            {state.data.items.map((h) => (
              <tr key={h.id}><td>{h.recipientName}</td><td>{h.kind}</td><td>{h.priority}</td><td>{h.status}</td><td>{when(h.createdAtUtc)}</td><td>{when(h.readAtUtc)}</td><td>{when(h.deletedAtUtc)}</td></tr>
            ))}
          </tbody>
        </table>
      )}
      {hasPermission("ViewMessageAuditContent") && <BreakGlassPanel />}
    </>
  );
}

function BreakGlassPanel() {
  const [target, setTarget] = useState<"message" | "thread">("message");
  const [id, setId] = useState("");
  const [reason, setReason] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<BreakGlassResult | null>(null);

  async function open(e: FormEvent) {
    e.preventDefault();
    setError(null); setResult(null);
    try { setResult(await breakGlass({ ...(target === "message" ? { messageId: id.trim() } : { threadId: id.trim() }), reason, confirmed })); }
    catch (err) { setError(err instanceof ApiError ? err.message : "The request could not be completed. Nothing was shown."); }
  }

  return (
    <section aria-labelledby="bg-title">
      <h2 id="bg-title">Open a private message (audited)</h2>
      <p>This opens one message or one thread. Your reason and the access are recorded and cannot be removed.</p>
      <form onSubmit={open} noValidate>
        <label>Open one{" "}
          <select value={target} onChange={(e) => setTarget(e.target.value as "message" | "thread")}><option value="message">message</option><option value="thread">thread</option></select>
        </label>
        <FormField label={target === "message" ? "Message id" : "Thread id"} value={id} onChange={(e) => setId(e.target.value)} />
        <FormField label="Reason (required)" value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} />
        <label><input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} /> I confirm I am opening private messages</label>
        <Button type="submit" variant="danger">Open</Button>
      </form>
      {error && <p role="alert">{error}</p>}
      {result && result.messages.map((m) => (
        <article key={m.id} aria-label={`Message from ${m.senderName}`}>
          <p><strong>{m.subject ?? "(no subject)"}</strong> from {m.senderName} to {m.recipientNames.join(", ")} <small>{when(m.sentAtUtc)}</small></p>
          <p style={{ whiteSpace: "pre-wrap" }}>{m.body}</p>
        </article>
      ))}
    </section>
  );
}
