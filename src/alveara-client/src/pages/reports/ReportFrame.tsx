import type { ReactNode } from "react";
import { Button } from "../../components/Button";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import type { ReportEnvelope } from "../../services/reportsApi";
import { formatMoment } from "../exports/exportFormat";
import type { Outcome } from "./useReport";

interface Props<T> {
  outcome: Outcome<ReportEnvelope<T>> | null;
  onRetry: () => void;
  /** What "nothing matched" means for this report, in words. */
  emptyText: string;
  children: (envelope: ReportEnvelope<T>) => ReactNode;
}

/**
 * Every report goes through this. While the answer for the current filter has not arrived it says it is loading; a failure is said in words with a way to try again (never a blank page or a stale answer); and a report
 * with nothing in its range says so plainly instead of showing a screen of zeros. A report that has something shows when it was made, how many days it covers and whether its numbers were read at one moment.
 */
export function ReportFrame<T>({ outcome, onRetry, emptyText, children }: Props<T>) {
  if (outcome === null) return <LoadingState label="Making the report…" />;
  if (!outcome.ok) return <ErrorState title="The report could not be shown" description={outcome.message} action={<Button onClick={onRetry}>Try again</Button>} />;
  const report = outcome.data;
  return (
    <div className="alv-rep__result">
      <p className="alv-rep__meta" data-testid="report-meta">
        {report.filter.from} to {report.filter.to} ({report.filter.days} {report.filter.days === 1 ? "day" : "days"}, {report.filter.timeZone}). Made {formatMoment(report.generatedAtUtc, report.filter.timeZone)}.{" "}
        {report.readConsistency === "Snapshot" ? "Every number was read at the same moment." : "The numbers were read one table after another, so a change made while the report ran could show in some and not others."}
      </p>
      {report.noData ? <EmptyState title="Nothing to report" description={emptyText} /> : children(report)}
    </div>
  );
}
