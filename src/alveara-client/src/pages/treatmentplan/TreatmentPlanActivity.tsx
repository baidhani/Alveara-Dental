import { useEffect, useState } from "react";
import { getTreatmentPlanHistory } from "../../services/treatmentPlansApi";
import type { PlanEvent, TreatmentPlan } from "../../services/treatmentPlansApi";
import { activityWords, when } from "./treatmentPlanRules";

/**
 * ALV-N023 (F-181, F-190): everything recorded on a plan, in order, with who and when - not only the revisions: each answer (with its reason), each target date, each appointment linked or unlinked,
 * the reference code, renames and completions. It reads the plan's own history (nothing new is stored) and loads only when opened.
 */
export function TreatmentPlanActivity({ plan }: { plan: TreatmentPlan }) {
  const [open, setOpen] = useState(false);
  const [events, setEvents] = useState<PlanEvent[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    getTreatmentPlanHistory(plan.id, controller.signal).then((e) => { setEvents(e); setFailed(false); }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [open, plan.id, plan.rowVersion, plan.updatedAtUtc, plan.items]);

  if (!open) return <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setOpen(true)}>Show activity</button>;
  const code = (itemId: string | null) => plan.items.find((i) => i.id === itemId)?.procedureCode;
  return (
    <section className="alv-tp__activity" aria-label={`Activity of ${plan.title}`}>
      <h4>Activity</h4>
      {failed ? <p className="alv-dx__message" role="alert">The activity could not be loaded. Nothing is lost; try again.</p>
        : events === null ? <p className="alv-clinical__meta">Loading the activity…</p>
        : (
          <ol className="alv-tp__activity-list">
            {events.map((e) => (
              <li key={e.eventNumber}>
                <strong>{activityWords(e.changeType)}</strong>{code(e.itemId) ? ` · ${code(e.itemId)}` : ""}{e.title ? ` · ${e.title}` : ""}
                <span className="alv-clinical__meta"> · {e.actorName ?? "Unknown"} on {when(e.occurredAtUtc)}{e.reason ? ` · Reason: ${e.reason}` : ""}</span>
              </li>
            ))}
          </ol>
        )}
      <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setOpen(false)}>Hide activity</button>
    </section>
  );
}
