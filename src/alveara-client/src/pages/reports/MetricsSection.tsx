import { SafeLink } from "../../components/SafeLink";
import { useAuth } from "../../contexts/AuthContext";
import { getMeasurement } from "../../services/reportsApi";
import type { ReportQuery } from "../../services/reportsApi";
import { formatMoment } from "../exports/exportFormat";
import { ReportFrame } from "./ReportFrame";
import { percent, queryKey, searchFromQuery } from "./reportFormat";
import { useReport } from "./useReport";

/** Where a person can see the records behind a metric: the report that lists them (only for metrics whose records a report lists). */
const RECORDS: Record<string, { report: string; label: string }> = {
  cancellation_rate: { report: "cancellations", label: "Cancellations and no-shows" },
  no_show_rate: { report: "cancellations", label: "Cancellations and no-shows" },
  late_follow_ups: { report: "follow-ups", label: "Recall and follow-up" },
};

/**
 * The measured metrics of the period. Each is a count out of a number of records: the "out of" is the sample size, and a metric with nothing counted shows "Not available", never zero. How each is defined (what is counted,
 * out of what, from which records) is one press away, with the definition version, because a baseline taken under another version cannot be compared with this number. Where a report lists the records behind a metric
 * there is a link to it for the same period.
 */
export function MetricsSection({ query }: { query: ReportQuery }) {
  const { hasPermission } = useAuth();
  const { outcome, again } = useReport((signal) => getMeasurement(query, undefined, signal), queryKey(query));

  return (
    <section aria-labelledby="alv-rep-metrics">
      <h2 id="alv-rep-metrics">Measured metrics for this period</h2>
      <ReportFrame outcome={outcome} onRetry={again} emptyText="Nothing was counted in this period, so no metric has a value. A metric with nothing counted shows Not available, never zero.">
        {(report) => (
          <>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Measured metrics for the period</caption>
              <thead><tr><th scope="col">Metric</th><th scope="col">Counted</th><th scope="col">Out of</th><th scope="col">Rate</th><th scope="col">Definition</th></tr></thead>
              <tbody>
                {report.data.metrics.map((m) => (
                  <tr key={m.key}>
                    <th scope="row">{m.name}</th>
                    <td>{m.numeratorCount.toLocaleString()}</td>
                    <td>{m.denominatorCount.toLocaleString()}</td>
                    <td>{percent(m.rate)}</td>
                    <td>Version {m.definitionVersion}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="alv-rep__hint">Follow-up and note metrics are judged at {formatMoment(report.data.asOfUtc, report.filter.timeZone)}.</p>
            <details>
              <summary>How each metric is defined</summary>
              <dl>
                {report.data.metrics.map((m) => (
                  <div key={m.key}>
                    <dt>{m.name} (version {m.definitionVersion})</dt>
                    <dd>
                      Counted: {m.numerator}. Out of: {m.denominator}. Source: {m.source}.
                      {RECORDS[m.key] && hasPermission("ViewOperationalReports") && <> <SafeLink to={`/reports/${RECORDS[m.key].report}?${searchFromQuery(query).toString()}`}>See the records in the {RECORDS[m.key].label} report</SafeLink></>}
                    </dd>
                  </div>
                ))}
              </dl>
            </details>
          </>
        )}
      </ReportFrame>
    </section>
  );
}
