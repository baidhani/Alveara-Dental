import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { listMine } from "../../services/followUpsApi";
import type { FollowUpFilter, FollowUpPage } from "../../services/followUpsApi";
import { Button } from "../../components/Button";
import { formatWhen } from "./followUpFormat";
import "./followups.css";

const FILTERS: { id: FollowUpFilter; label: string }[] = [{ id: "open", label: "Open" }, { id: "closed", label: "Closed" }, { id: "all", label: "All" }];

/** STORY-009: the follow-ups the signed-in person is responsible for, soonest due first, each with a link to the patient follow-ups where it is worked. Overdue is said in words. */
export function MyFollowUpsPage() {
  const [filter, setFilter] = useState<FollowUpFilter>("open");
  const [page, setPage] = useState<FollowUpPage | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    listMine(filter, controller.signal).then((p) => { setPage(p); setFailed(false); }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [filter]);

  if (failed && page === null) return <ErrorState title="Could not load your follow-ups" description="Do not assume you have none. Check your connection and reload the page." />;
  if (page === null) return <LoadingState label="Loading your follow-ups…" />;

  return (
    <section className="alv-clinical alv-fu" aria-labelledby="alv-fu-mine-title">
      <h1 id="alv-fu-mine-title" className="alv-workspace__section-title">My follow-ups</h1>
      <p className="alv-clinical__meta">The follow-ups you are responsible for. Open a patient&apos;s follow-ups to complete, change or cancel one.</p>
      {failed && <p role="alert" className="alv-fu-error">Could not refresh. What is shown may be out of date.</p>}
      <div role="group" aria-label="Show">
        {FILTERS.map((f) => (
          <Button key={f.id} type="button" variant={filter === f.id ? "primary" : "secondary"} aria-pressed={filter === f.id} onClick={() => setFilter(f.id)}>{f.label}</Button>
        ))}
      </div>
      {page.items.length === 0 ? <EmptyState title="Nothing here" description="You have no follow-ups in this view." /> : (
        <ul className="alv-fu-list" aria-label={`${filter} follow-ups`}>
          {page.items.map((t) => (
            <li key={t.id} className="alv-fu-item">
              <p className="alv-fu-item__title">
                <strong>{t.title}</strong> <span className={`alv-fu-badge alv-fu-badge--${t.status.toLowerCase()}`}>{t.status}</span>
                {t.status === "Open" && t.overdue && <span className="alv-fu-badge alv-fu-badge--overdue">Overdue</span>}
              </p>
              <p className="alv-fu-item__meta">Due {formatWhen(t.dueAtUtc)} · <Link to={`/patients/${t.patientId}/follow-ups`}>Open this patient&apos;s follow-ups</Link></p>
            </li>
          ))}
        </ul>
      )}
      {page.more && <p className="alv-clinical__meta">Showing the first {page.count}. Narrow the list with Open or Closed.</p>}
    </section>
  );
}
