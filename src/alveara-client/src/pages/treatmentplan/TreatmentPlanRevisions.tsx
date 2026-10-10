import { useEffect, useId, useState } from "react";
import { LoadingState } from "../../components/StatePatterns";
import { compareRevisions, getRevision, getRevisions } from "../../services/treatmentPlansApi";
import type { RevisionComparison, RevisionDetail, RevisionSummary, TreatmentPlan } from "../../services/treatmentPlansApi";
import { TreatmentPlanComparison } from "./TreatmentPlanComparison";
import { TreatmentPlanRevisionView } from "./TreatmentPlanRevisionView";
import { changeWords, formatFee, when } from "./treatmentPlanRules";

type Shown = { kind: "revision"; value: RevisionDetail } | { kind: "comparison"; value: RevisionComparison } | null;

/**
 * ALV-015-C01: a plan's revision history. Every change to a plan's content saved a version and none was ever edited, so this lists them all (what produced each, who, when, why, and what the plan came to),
 * opens any one exactly as it was saved, and compares any two. It loads only when opened, says so when it cannot load rather than showing an empty list, and reloads when the plan moves to a new revision.
 * Anyone who may read the plan may read its history.
 */
export function TreatmentPlanRevisions({ plan }: { plan: TreatmentPlan }) {
  const [open, setOpen] = useState(false);
  const [revisions, setRevisions] = useState<RevisionSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [shown, setShown] = useState<Shown>(null);
  const [from, setFrom] = useState(0);
  const [to, setTo] = useState(0);
  const [note, setNote] = useState<string | null>(null);
  const uid = useId();

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    getRevisions(plan.id, controller.signal)
      .then((r) => { setRevisions(r); setFailed(false); setFrom(r.length > 1 ? r[r.length - 2].revisionNumber : (r[0]?.revisionNumber ?? 0)); setTo(r[r.length - 1]?.revisionNumber ?? 0); })
      .catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [open, plan.id, plan.currentRevision]);

  async function show(action: () => Promise<Shown>) {
    setNote(null);
    try {
      setShown(await action());
    } catch {
      setNote("Could not load that. Nothing on screen has changed; try again.");
    }
  }

  return (
    <div className="alv-tp__history">
      <button type="button" className="btn btn-outline-secondary btn-sm" aria-expanded={open} onClick={() => { setOpen(!open); setShown(null); }}>
        {open ? "Hide revision history" : "Show revision history"}
      </button>
      {open && failed && revisions === null && <p className="alv-clinical__meta" role="alert">Could not load the revision history. Do not assume there is none; check your connection and try again.</p>}
      {open && !failed && revisions === null && <LoadingState label="Loading the revision history…" />}
      {open && revisions !== null && (
        <>
          <ol className="alv-tp__items" aria-label={`Revisions of ${plan.title}`}>
            {revisions.map((r) => (
              <li key={r.revisionNumber} className="alv-tp__item">
                <strong>Revision {r.revisionNumber}</strong>{r.revisionNumber === plan.currentRevision && <> <span className="alv-dx__badge">Current</span></>}: {changeWords(r.changeType)}
                <span className="alv-clinical__meta"> · {r.actorName ?? "Unknown"} on {when(r.createdAtUtc)} · {r.title} · {r.procedureCount} {r.procedureCount === 1 ? "procedure" : "procedures"}, {formatFee(r.estimateTotal)}, {r.phaseCount} {r.phaseCount === 1 ? "phase" : "phases"}{r.reason ? ` · Reason: ${r.reason}` : ""}</span>
                {" "}<button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => void show(async () => ({ kind: "revision", value: await getRevision(plan.id, r.revisionNumber) }))}>View revision {r.revisionNumber}</button>
              </li>
            ))}
          </ol>
          {revisions.length > 1 && (
            <form className="alv-dx__form" aria-label="Compare two revisions" onSubmit={(e) => { e.preventDefault(); void show(async () => ({ kind: "comparison", value: await compareRevisions(plan.id, from, to) })); }}>
              <label htmlFor={`${uid}-from`}>Compare revision
                <select id={`${uid}-from`} value={from} onChange={(e) => setFrom(Number(e.target.value))}>{revisions.map((r) => <option key={r.revisionNumber} value={r.revisionNumber}>{r.revisionNumber}</option>)}</select>
              </label>
              <label htmlFor={`${uid}-to`}>with revision
                <select id={`${uid}-to`} value={to} onChange={(e) => setTo(Number(e.target.value))}>{revisions.map((r) => <option key={r.revisionNumber} value={r.revisionNumber}>{r.revisionNumber}</option>)}</select>
              </label>
              <div className="alv-clinical__row-actions"><button type="submit" className="btn btn-primary">Compare</button></div>
            </form>
          )}
          {note && <p className="alv-clinical__meta" role="alert">{note}</p>}
          {shown?.kind === "revision" && <TreatmentPlanRevisionView revision={shown.value} />}
          {shown?.kind === "comparison" && <TreatmentPlanComparison comparison={shown.value} />}
        </>
      )}
    </div>
  );
}
