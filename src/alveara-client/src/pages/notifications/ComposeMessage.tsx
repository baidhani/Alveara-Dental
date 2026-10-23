import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { ApiError } from "../../services/authApi";
import { findRecipients, problemsOf, sendMessage } from "../../services/notificationsApi";
import type { MessageView, Priority, Problem, RecipientChoice } from "../../services/notificationsApi";

const newKey = () => (crypto.randomUUID ? crypto.randomUUID() : `k-${Date.now()}-${Math.random().toString(16).slice(2)}`);

/**
 * ALV-N015: write a message, or a reply (which joins the thread; the writer picks the recipients again). Plain text only. The idempotency key is made once per attempt and kept
 * for a retry after a failure, so pressing Send again cannot send it twice; changing the form starts a new attempt.
 */
export function ComposeMessage({ reply, onSent }: { reply?: { message: MessageView }; onSent: (m: MessageView) => void }) {
  const [people, setPeople] = useState<RecipientChoice[]>([]);
  const [chosen, setChosen] = useState<string[]>([]);
  const [search, setSearch] = useState("");
  const [subject, setSubject] = useState(reply?.message.subject ? `Re: ${reply.message.subject}`.slice(0, 120) : "");
  const [body, setBody] = useState("");
  const [priority, setPriority] = useState<Priority>("Normal");
  const [problems, setProblems] = useState<Problem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const key = useRef(newKey());
  const changed = () => { key.current = newKey(); };

  useEffect(() => {
    let live = true;
    findRecipients(search).then((p) => { if (live) setPeople(p); }).catch(() => { if (live) setPeople([]); });
    return () => { live = false; };
  }, [search]);

  const problem = (field: string) => problems.find((p) => p.field.toLowerCase() === field.toLowerCase())?.message;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    setBusy(true); setError(null); setProblems([]);
    try {
      const result = await sendMessage({ recipientUserIds: chosen, subject, body, priority, inReplyToMessageId: reply?.message.id ?? null }, key.current);
      key.current = newKey();
      onSent(result.message);
    } catch (err) {
      setProblems(problemsOf(err));
      setError(err instanceof ApiError ? err.message : "The message could not be sent. Try again; it will not be sent twice.");
    } finally { setBusy(false); }
  }

  return (
    <form onSubmit={submit} aria-label={reply ? "Reply" : "New message"} noValidate>
      {error && <p role="alert">{error}</p>}
      <fieldset>
        <legend>To</legend>
        <FormField label="Find a person" value={search} onChange={(e) => setSearch(e.target.value)} />
        {problem("recipientUserIds") && <p role="alert">{problem("recipientUserIds")}</p>}
        <ul>
          {people.map((p) => (
            <li key={p.userId}>
              <label>
                <input type="checkbox" checked={chosen.includes(p.userId)}
                  onChange={(e) => { changed(); setChosen(e.target.checked ? [...chosen, p.userId] : chosen.filter((x) => x !== p.userId)); }} />
                {p.name} ({p.role})
              </label>
            </li>
          ))}
        </ul>
      </fieldset>
      <FormField label="Subject" value={subject} maxLength={120} error={problem("subject")} onChange={(e) => { changed(); setSubject(e.target.value); }} />
      <div className="alv-form-field">
        <label htmlFor="msg-body" className="alv-form-field__label">Message (plain text, up to 2000 characters)</label>
        <textarea id="msg-body" rows={6} maxLength={2000} value={body} aria-invalid={problem("body") ? true : undefined}
          onChange={(e) => { changed(); setBody(e.target.value); }} />
        {problem("body") && <p role="alert">{problem("body")}</p>}
      </div>
      <div className="alv-form-field">
        <label htmlFor="msg-priority" className="alv-form-field__label">Priority</label>
        <select id="msg-priority" value={priority} onChange={(e) => { changed(); setPriority(e.target.value as Priority); }}>
          <option>Normal</option><option>Important</option><option>Urgent</option>
        </select>
      </div>
      <Button type="submit" variant="primary" disabled={busy}>{busy ? "Sending…" : "Send"}</Button>
    </form>
  );
}
