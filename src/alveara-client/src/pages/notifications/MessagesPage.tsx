import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { PageHeader } from "../../components/PageHeader";
import { Button, ButtonLink } from "../../components/Button";
import { LoadingState, EmptyState, ErrorState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { ApiError } from "../../services/authApi";
import { getMessage, getThread, listMessages, messagePatientLink } from "../../services/notificationsApi";
import type { MessageListItem, MessageView, Paged } from "../../services/notificationsApi";
import { ComposeMessage } from "./ComposeMessage";
import "./Notifications.css";

const when = (iso: string) => new Date(iso).toLocaleString();

/** ALV-N015: the message inbox and sent box. Opening a message marks the caller's own notification read; read receipts are shown to the sender only. */
export function MessagesPage() {
  const { hasPermission } = useAuth();
  const [box, setBox] = useState<"inbox" | "sent">("inbox");
  const [composing, setComposing] = useState(false);
  const [state, setState] = useState<{ kind: "loading" } | { kind: "error" } | { kind: "loaded"; data: Paged<MessageListItem> }>({ kind: "loading" });
  const navigate = useNavigate();
  const latest = useRef(0);                                                              // an answer to an earlier request (another box) must never replace the current one

  const load = useCallback(async () => {
    const mine = ++latest.current;
    setState({ kind: "loading" });
    try { const data = await listMessages(box); if (mine === latest.current) setState({ kind: "loaded", data }); }
    catch { if (mine === latest.current) setState({ kind: "error" }); }
  }, [box]);
  useEffect(() => { void load(); }, [load]);

  return (
    <>
      <PageHeader
        title="Messages"
        description="Internal messages for practice operations. Plain text only: do not put clinical details in a message."
        actions={hasPermission("SendMessages") ? <Button variant="primary" onClick={() => setComposing(!composing)}>{composing ? "Cancel" : "New message"}</Button> : undefined}
      />
      {composing && <ComposeMessage onSent={(m) => { setComposing(false); navigate(`/messages/${m.id}`); }} />}
      <div role="tablist" aria-label="Message box">
        {(["inbox", "sent"] as const).map((b) => (
          <button key={b} role="tab" type="button" aria-selected={box === b} onClick={() => setBox(b)}>{b === "inbox" ? "Inbox" : "Sent"}</button>
        ))}
      </div>
      {state.kind === "loading" && <LoadingState label="Loading messages…" />}
      {state.kind === "error" && <ErrorState title="Could not load messages" action={<Button onClick={load}>Retry</Button>} />}
      {state.kind === "loaded" && state.data.items.length === 0 && <EmptyState title={box === "inbox" ? "No messages" : "Nothing sent"} />}
      {state.kind === "loaded" && state.data.items.length > 0 && (
        <ul>
          {state.data.items.map((m) => (
            <li key={m.id}>
              <Link to={`/messages/${m.id}`} className="alv-notif__link">{m.isUnread ? <strong>{m.subject ?? "(no subject)"}</strong> : (m.subject ?? "(no subject)")}</Link>{" "}
              {m.priority !== "Normal" && <em>{m.priority}</em>} {box === "inbox" ? `from ${m.senderName}` : `to ${m.recipientCount}, read by ${m.readCount}`} <small>{when(m.sentAtUtc)}</small>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

/** One message and the rest of its thread, with a reply form. */
export function MessageDetailPage() {
  const { id } = useParams();
  const { hasPermission } = useAuth();
  const navigate = useNavigate();
  const [state, setState] = useState<{ kind: "loading" } | { kind: "missing" } | { kind: "error" } | { kind: "loaded"; message: MessageView; thread: MessageView[] }>({ kind: "loading" });
  const [replying, setReplying] = useState(false);
  const [note, setNote] = useState<string | null>(null);

  const load = useCallback(async () => {
    setState({ kind: "loading" });
    try {
      const message = await getMessage(id!);
      const thread = await getThread(message.threadId);
      setState({ kind: "loaded", message, thread });
    } catch (err) { setState(err instanceof ApiError && err.status === 404 ? { kind: "missing" } : { kind: "error" }); }
  }, [id]);
  useEffect(() => { void load(); }, [load]);

  async function openPatient(messageId: string) {
    setNote(null);
    try { navigate((await messagePatientLink(messageId)).path); }
    catch (err) { setNote(err instanceof ApiError && err.status === 403 ? "You cannot open that patient's record from here." : "Could not open it. Try again."); }
  }

  if (state.kind === "loading") return <LoadingState label="Loading message…" />;
  if (state.kind === "missing") return <EmptyState title="That message was not found" description="It may not be addressed to you." />;
  if (state.kind === "error") return <ErrorState title="Could not load the message" action={<Button onClick={load}>Retry</Button>} />;

  return (
    <>
      <PageHeader title={state.message.subject ?? "(no subject)"} actions={<ButtonLink to="/messages">Back to messages</ButtonLink>} />
      {note && <p role="status">{note}</p>}
      {state.thread.map((m) => (
        <article key={m.id} aria-label={`Message from ${m.senderName}`}>
          <p><strong>{m.isFromMe ? "You" : m.senderName}</strong> <small>{when(m.sentAtUtc)}</small> {m.priority !== "Normal" && <em>{m.priority}</em>}</p>
          <p style={{ whiteSpace: "pre-wrap" }}>{m.body}</p>
          {m.hasPatientLink && <Button onClick={() => openPatient(m.id)}>Open patient</Button>}
          {m.isFromMe && (
            <p><small>{m.recipients.map((r) => `${r.name}: ${r.readAtUtc ? `read ${when(r.readAtUtc)}` : "not read yet"}`).join("; ")}</small></p>
          )}
        </article>
      ))}
      {hasPermission("SendMessages") && (replying
        ? <ComposeMessage reply={{ message: state.message }} onSent={() => { setReplying(false); void load(); }} />
        : <Button onClick={() => setReplying(true)}>Reply</Button>)}
    </>
  );
}
