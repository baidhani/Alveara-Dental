import { useState } from "react";
import { Button } from "../../components/Button";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { getComparison } from "../../services/reportsApi";
import type { Baseline, MetricComparison, MetricSide, ReportQuery } from "../../services/reportsApi";
import { percent, queryKey } from "./reportFormat";
import { useReport } from "./useReport";
import type { Outcome } from "./useReport";

const side = (s: MetricSide | null) => (s === null ? "None" : `${percent(s.rate)} (${s.numeratorCount.toLocaleString()} of ${s.denominatorCount.toLocaleString()})`);

/**
 * The change is said only for a metric the server calls comparable, and only as Lower, Higher or the Same, by how many percentage points and (when the baseline rate is not zero) as a share of the baseline rate. It is
 * never called an improvement, a worsening or an effect. A metric that cannot be compared shows its own value, the reason in words and the words "No change shown".
 */
function changeText(c: MetricComparison): string {
  if (c.status !== "comparable" || c.movement === null || c.absoluteChange === null) return "No change shown";
  if (c.movement === "Same") return "The same";
  const points = `${Math.abs(c.absoluteChange * 100).toFixed(1)} percentage points`;
  const share = c.relativeChange === null ? "" : ` (${Math.abs(c.relativeChange * 100).toFixed(0)}% of the baseline rate)`;
  return `${c.movement} by ${points}${share}`;
}

/** The comparison of this period with a baseline (the latest unless one is chosen), the things that cannot be measured and the sentence that a difference is not a cause. */
export function ComparisonSection({ query, baselines, refresh }: { query: ReportQuery; baselines: Outcome<Baseline[]> | null; refresh: number }) {
  const [baselineId, setBaselineId] = useState("");
  const { outcome, again } = useReport((signal) => getComparison(query, baselineId || undefined, signal), `${queryKey(query)}|${baselineId}|${refresh}`);
  const list = baselines?.ok ? baselines.data : [];

  return (
    <section aria-labelledby="alv-rep-comparison">
      <h2 id="alv-rep-comparison">Comparison with a baseline</h2>
      {list.length > 1 && (
        <label>Compare with
          <select value={baselineId} onChange={(e) => setBaselineId(e.target.value)}>
            <option value="">The latest baseline</option>
            {list.map((b) => <option key={b.id} value={b.id}>{b.name} ({b.from} to {b.to})</option>)}
          </select>
        </label>
      )}
      {outcome === null && <LoadingState label="Comparing…" />}
      {outcome && !outcome.ok && <ErrorState title="The comparison could not be shown" description={outcome.message} action={<Button onClick={again}>Try again</Button>} />}
      {outcome?.ok && (() => {
        const d = outcome.data.data;
        return (
          <>
            {d.baseline
              ? <p>Compared with <strong>{d.baseline.name}</strong> ({d.baseline.from} to {d.baseline.to}). A metric is compared only when that period ended before this one started, the metric is defined the same way, and both periods have at least {d.minimumSample} records.</p>
              : <p>There is no baseline yet, so nothing can be compared and no change is shown.</p>}
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Each metric in this period against the baseline</caption>
              <thead><tr><th scope="col">Metric</th><th scope="col">Baseline</th><th scope="col">This period</th><th scope="col">Change</th><th scope="col">Why</th></tr></thead>
              <tbody>
                {d.comparisons.map((c) => (
                  <tr key={c.key}>
                    <th scope="row">{c.name}</th>
                    <td>{side(c.baseline)}</td>
                    <td>{side(c.current)}</td>
                    <td>{changeText(c)}</td>
                    <td>{c.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="alv-rep__hint">{d.note}</p>
            <h3>What cannot be measured yet</h3>
            <ul>{d.notMeasurable.map((n) => <li key={n.key}><strong>{n.name}.</strong> {n.reason}</li>)}</ul>
          </>
        );
      })()}
    </section>
  );
}
