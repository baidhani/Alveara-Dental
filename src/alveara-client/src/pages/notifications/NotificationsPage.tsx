import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { PageHeader } from "../../components/PageHeader";
import { Button, ButtonLink } from "../../components/Button";
import { LoadingState, EmptyState, ErrorState } from "../../components/StatePatterns";
import { ApiError } from "../../services/authApi";
import { clearHistory, hideNotification, listNotifications, markAllRead, markRead, notificationPatientLink } from "../../services/notificationsApi";
import type { NotificationView, Paged } from "../../services/notificationsApi";

type View = "unread" | "history";
type State = { kind: "loading" } | { kind: "error" } | { kind: "loaded"; data: Paged<NotificationView> };

const KIND_LABEL: Record<string, string> = { "document.duplicate_added": "A duplicate document was added", "message.received": "New message" };
const when = (iso: string) => new Date(iso).toLocaleString();

/** ALV-N015: the caller's own notifications. Unread ones can be marked read; only read ones can be hidden (a hidden entry is kept, never deleted). */
export function NotificationsPage() {
  const [view, setView] = useState<View>("unread");
  const [state, setState] = useState<State>({ kind: "loading" });
  const [message, setMessage] = useState<string | null>(null);
  const navigate = useNavigate();
  const latest = useRef(0);                                                              // an answer to an earlier request (another tab) must never replace the current one

  const load = useCallback(async () => {
    const mine = ++latest.current;
    setState({ kind: "loading" });
    try { const data = await listNotifications(view); if (mine === latest.current) setState({ kind: "loaded", data }); }
    catch { if (mine === latest.current) setState({ kind: "error" }); }
  }, [view]);
  useEffect(() => { void load(); }, [load]);

  async function act(fn: () => Promise<unknown>, done?: string) {
    setMessage(null);
    try { await fn(); if (done) setMessage(done); await load(); }
    catch (err) { setMessage(err instanceof ApiError ? err.message : "That did not work. Try again."); }
  }

  async function open(n: NotificationView) {
    setMessage(null);
    try {
      const target = n.messageId ? undefined : await notificationPatientLink(n.id);
      await markRead(n.id);
      navigate(n.messageId ? `/messages/${n.messageId}` : target!.path);
    } catch (err) {
      setMessage(err instanceof ApiError && err.status === 403 ? "You cannot open that patient's record from here." : "Could not open it. Try again.");
    }
  }

  return (
    <>
      <PageHeader
        title="Notifications"
        description="Notices for you. An unread notice has to be read before it can be hidden; nothing is ever deleted."
        actions={<ButtonLink to="/messages">Messages</ButtonLink>}
      />
      <div role="tablist" aria-label="Notification view">
        {(["unread", "history"] as View[]).map((v) => (
          <button key={v} role="tab" type="button" aria-selected={view === v} onClick={() => setView(v)}>{v === "unread" ? "Unread" : "History"}</button>
        ))}
      </div>
      {message && <p role="status">{message}</p>}
      {state.kind === "loading" && <LoadingState label="Loading notifications…" />}
      {state.kind === "error" && <ErrorState title="Could not load notifications" action={<Button onClick={load}>Retry</Button>} />}
      {state.kind === "loaded" && (
        <>
          <p>
            {view === "unread" && state.data.total > 0 && <Button onClick={() => act(markAllRead, "All marked read.")}>Mark all read</Button>}
            {view === "history" && state.data.total > 0 && <Button onClick={() => act(clearHistory, "History cleared from your list.")}>Clear history</Button>}
          </p>
          {state.data.items.length === 0 && <EmptyState title={view === "unread" ? "Nothing unread" : "No history"} />}
          <ul>
            {state.data.items.map((n) => (
              <li key={n.id}>
                <strong>{n.priority !== "Normal" ? `${n.priority}: ` : ""}{KIND_LABEL[n.kind] ?? "Notification"}</strong>
                {n.senderName && <> from {n.senderName}</>} <small>{when(n.createdAtUtc)}</small>{" "}
                {(n.messageId || n.hasPatientLink) && <Button onClick={() => open(n)}>{n.messageId ? "Open message" : "Open patient"}</Button>}
                {!n.readAtUtc && <Button onClick={() => act(() => markRead(n.id))}>Mark read</Button>}
                {n.readAtUtc && view === "history" && <Button onClick={() => act(() => hideNotification(n.id))}>Hide</Button>}
              </li>
            ))}
          </ul>
        </>
      )}
    </>
  );
}
