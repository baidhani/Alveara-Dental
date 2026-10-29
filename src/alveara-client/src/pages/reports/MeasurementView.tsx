import { useState } from "react";
import { listBaselines } from "../../services/reportsApi";
import type { ReportQuery } from "../../services/reportsApi";
import { BaselineSection } from "./BaselineSection";
import { ComparisonSection } from "./ComparisonSection";
import { MetricsSection } from "./MetricsSection";
import { useReport } from "./useReport";

/**
 * The measurement panel: the measured metrics of the period, the baselines and the form to set one, and the comparison. It is about not claiming more than the numbers show: every metric says what was counted out of what,
 * a comparison shows a change only when it can honestly be made, and a difference is never called a cause. Setting a baseline refreshes the list and the comparison.
 */
export function MeasurementView({ query }: { query: ReportQuery }) {
  const [refresh, setRefresh] = useState(0);
  const { outcome: baselines, again } = useReport((signal) => listBaselines(signal), `baselines#${refresh}`);

  return (
    <div className="alv-rep__measurement">
      <MetricsSection query={query} />
      <BaselineSection baselines={baselines} onRetry={again} onSaved={() => setRefresh((n) => n + 1)} />
      <ComparisonSection query={query} baselines={baselines} refresh={refresh} />
    </div>
  );
}
