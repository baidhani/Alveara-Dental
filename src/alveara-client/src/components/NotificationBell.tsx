import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { getSummary } from "../services/notificationsApi";
import type { NotificationSummary } from "../services/notificationsApi";
import "./NotificationBell.css";

export const NOTIFICATION_POLL_MS = 60_000;

type Check = { kind: "loading" } | { kind: "failed"; last: NotificationSummary | null } | { kind: "ok"; summary: NotificationSummary };

/**
 * ALV-N015: the bell and the Important/Urgent banner. It asks the server for counts only, every 60 seconds and when the window regains focus.
 * A failed check says so ("could not check") and never shows zero; the banner text is generic - it never carries a patient, a file or a message.
 */
export function NotificationBell() {
  const [check, setCheck] = useState<Check>({ kind: "loading" });
  const last = useRef<NotificationSummary | null>(null);

  const refresh = useCallback(async () => {
    try {
      const summary = await getSummary();
      last.current = summary;
      setCheck({ kind: "ok", summary });
    } catch {
      setCheck({ kind: "failed", last: last.current });
    }
  }, []);

  useEffect(() => {
    void refresh();
    const timer = window.setInterval(() => void refresh(), NOTIFICATION_POLL_MS);
    const onFocus = () => void refresh();
    window.addEventListener("focus", onFocus);
    return () => { window.clearInterval(timer); window.removeEventListener("focus", onFocus); };
  }, [refresh]);

  const summary = check.kind === "ok" ? check.summary : null;
  const label = check.kind === "ok" ? `Notifications, ${check.summary.unread} unread` : check.kind === "failed" ? "Notifications, could not check" : "Notifications, checking";

  return (
    <>
      {summary && (summary.unreadUrgent > 0 || summary.unreadImportant > 0) && (
        <div className={`alv-bell-banner${summary.unreadUrgent > 0 ? " alv-bell-banner--urgent" : ""}`} role={summary.unreadUrgent > 0 ? "alert" : "status"}>
          {summary.unreadUrgent > 0 ? "You have an urgent notification." : "You have an important notification."}{" "}
          <Link to="/notifications">Open notifications</Link>
        </div>
      )}
      <Link to="/notifications" className="alv-bell" aria-label={label}>
        <span aria-hidden="true">Notifications</span>
        {summary && summary.unread > 0 && <span className="alv-bell__count" aria-hidden="true">{summary.unread > 99 ? "99+" : summary.unread}</span>}
        {check.kind === "failed" && <span className="alv-bell__failed">Could not check</span>}
      </Link>
    </>
  );
}
