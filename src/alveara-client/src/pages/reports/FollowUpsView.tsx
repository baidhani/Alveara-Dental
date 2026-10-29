import { useState } from "react";
import { getFollowUps } from "../../services/reportsApi";
import type { FollowUpCounts, FollowUpDrill, ReportQuery } from "../../services/reportsApi";
import { formatMoment } from "../exports/exportFormat";
import { NumberButton } from "./NumberButton";
import { FollowUpRecords } from "./RecordsPanel";
import { ReportFrame } from "./ReportFrame";
import { queryKey, words } from "./reportFormat";
import { useReport } from "./useReport";

type Spec = { title: string; drill: FollowUpDrill };

const OUTCOMES: { key: keyof FollowUpCounts; outcome?: string; header: string }[] = [
  { key: "due", header: "Due" },
  { key: "completed", outcome: "Completed", header: "Completed" },
  { key: "cancelled", outcome: "Cancelled", header: "Cancelled" },
  { key: "closedBySystem", outcome: "ClosedBySystem", header: "Closed by the system" },
  { key: "openNotYetDue", outcome: "OpenNotYetDue", header: "Open, not yet due" },
  { key: "openOverdue", outcome: "OpenOverdue", header: "Open and overdue" },
  { key: "completedLate", outcome: "CompletedLate", header: "Completed late" },
  { key: "openUnassigned", outcome: "OpenUnassigned", header: "Open, no owner" },
];

/**
 * The recall and follow-up report: the tasks DUE in the period, judged at the moment shown ("Judged at"). Completed, cancelled, closed by the system, open and not yet due, and open and overdue add up to the tasks due;
 * completed late and open with no owner are further cuts. Judged later, a task that was not yet due can be overdue, so the moment is always shown. Every count lists its tasks (kind, state and times, never a title or note).
 */
export function FollowUpsView({ query }: { query: ReportQuery }) {
  const { outcome, again } = useReport((signal) => getFollowUps(query, undefined, signal), queryKey(query));
  const [spec, setSpec] = useState<Spec | null>(null);

  return (
    <ReportFrame outcome={outcome} onRetry={again} emptyText="No follow-up task is due in this period.">
      {(report) => {
        const d = report.data;
        const open = (title: string, drill: FollowUpDrill) => setSpec({ title, drill: { ...drill, asOf: d.asOfUtc } });
        const row = (name: string, counts: FollowUpCounts, kind?: string) => (
          <tr key={name}>
            <th scope="row">{name}</th>
            {OUTCOMES.map((o) => (
              <td key={o.key}><NumberButton value={counts[o.key]} label={`${name}, ${o.header.toLowerCase()}`} onOpen={() => open(`${name}: ${o.header.toLowerCase()}`, { kind, outcome: o.outcome })} /></td>
            ))}
          </tr>
        );
        return (
          <>
            <p className="alv-rep__hint">Judged at {formatMoment(d.asOfUtc, report.filter.timeZone)}. A task is overdue when it is still open and was due before that moment.</p>
            <p className={d.reconciled ? "alv-rep__hint" : "alv-rep__problem"} role={d.reconciled ? undefined : "alert"}>
              {d.reconciled ? "The outcomes add up to the tasks due." : "The outcomes do not add up to the tasks due. Do not rely on these numbers; tell your administrator."}
            </p>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Follow-up tasks due in the period, by kind and outcome</caption>
              <thead><tr><th scope="col">Kind</th>{OUTCOMES.map((o) => <th key={o.key} scope="col">{o.header}</th>)}</tr></thead>
              <tbody>
                {d.kinds.filter((k) => k.counts.due > 0).map((k) => row(words(k.kind), k.counts, k.kind))}
                {row("All kinds", d.totals)}
              </tbody>
            </table>
            {spec && <FollowUpRecords key={JSON.stringify(spec)} query={query} drill={spec.drill} title={spec.title} timeZone={report.filter.timeZone} onClose={() => setSpec(null)} />}
          </>
        );
      }}
    </ReportFrame>
  );
}
