import { useState } from "react";
import type { PerioChart } from "../../services/perioApi";
import { PerioChartLinks, PerioToothRecords } from "./PerioChartExtras";
import { PerioComparisonPanel } from "./PerioComparisonPanel";
import { displayTooth, toothName } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { SITE_NAMES } from "./perioChartRules";
import type { Site } from "./perioChartRules";
import { DEEP_POCKET_MM, summarize } from "./perioSummary";

interface Props {
  patientId: string;
  /** Newest first; null while loading. */
  charts: PerioChart[] | null;
  /** True when the earlier charts could not be loaded: say so, never show "none". */
  failed: boolean;
  numbering: NumberingSystem;
  canWrite: boolean;
  onRetry: () => void;
  onUseAsStart: (chart: PerioChart) => void;
  /** A chart changed on the server (a link was added): the list shows it as it now is. */
  onChartChanged: (chart: PerioChart) => void;
}

const when = (iso: string) => new Date(iso).toLocaleString();
const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/**
 * STORY-012: the charts on record, newest first, each opening to its readings and the figures read off them (the newest is open). A saved chart is never edited, so the only action is to start
 * a new chart from its values. No chart on record says exactly that and never implies healthy gums; a failed load says it could not load rather than showing an empty list.
 */
export function PerioHistory({ patientId, charts, failed, numbering, canWrite, onRetry, onUseAsStart, onChartChanged }: Props) {
  const [comparing, setComparing] = useState<string | null>(null);
  return (
    <section className="alv-perio__history" aria-labelledby="alv-perio-history-title">
      <h3 id="alv-perio-history-title">Charts on record</h3>
      {failed && (
        <div className="alv-perio__problems" role="alert">
          <p>Could not load the charts on record. Do not assume there are none. You can still enter and save a new chart.</p>
          <button type="button" className="btn btn-outline-secondary" onClick={onRetry}>Try again</button>
        </div>
      )}
      {!failed && charts === null && <p className="alv-clinical__meta" role="status">Loading the charts on record…</p>}
      {!failed && charts?.length === 0 && <p className="alv-clinical__meta">No periodontal chart has been recorded for this patient. That means none has been taken, not that the gums are healthy.</p>}
      {!failed && charts?.map((chart, i) => {
        const s = summarize(chart);
        const earlier = charts[i + 1];
        const withPus = chart.readings.some((r) => r.suppuration !== null && r.suppuration !== undefined);
        const withPlaque = chart.readings.some((r) => r.plaque !== null && r.plaque !== undefined);
        return (
          <details key={chart.id} className="alv-perio__chart" open={i === 0}>
            <summary>
              <strong>{when(chart.recordedAtUtc)}</strong> by {chart.recordedByName} — {plural(s.sites, "site", "sites")} on {plural(s.teeth, "tooth", "teeth")}, {s.bleedingPercent}% bleeding
              {i === 0 && " (latest)"}
            </summary>
            <ul className="alv-perio__figures" aria-label="Figures for this chart">
              <li>Bleeding on probing: <strong>{s.bleedingPercent}%</strong> of sites</li>
              <li>Sites {DEEP_POCKET_MM} mm or deeper: <strong>{s.deepSites}</strong></li>
              <li>Deepest site: <strong>{s.deepestMm} mm</strong></li>
            </ul>
            <div className="alv-perio__arch">
              <table className="alv-perio__table">
                <caption className="alv-perio__sr">Readings in the chart of {when(chart.recordedAtUtc)}</caption>
                <thead>
                  <tr><th scope="col">Tooth</th><th scope="col">Site</th><th scope="col">Probing depth (mm)</th><th scope="col">Recession (mm)</th><th scope="col">Attachment loss (mm)</th><th scope="col">Bleeding</th>{withPus && <th scope="col">Pus</th>}{withPlaque && <th scope="col">Plaque</th>}</tr>
                </thead>
                <tbody>
                  {chart.readings.map((r) => (
                    <tr key={`${r.toothKey}-${r.site}`}>
                      <th scope="row" title={toothName(r.toothKey)}>{displayTooth(r.toothKey, numbering)}</th>
                      <td><abbr title={SITE_NAMES[r.site as Site]}>{r.site}</abbr></td>
                      <td>{r.probingDepthMm}</td>
                      <td>{r.recessionMm}</td>
                      <td>{r.attachmentLossMm}</td>
                      <td>{r.bleeding ? "Yes" : "No"}</td>
                      {withPus && <td>{r.suppuration === null || r.suppuration === undefined ? "not assessed" : r.suppuration ? "Yes" : "No"}</td>}
                      {withPlaque && <td>{r.plaque === null || r.plaque === undefined ? "not assessed" : r.plaque ? "Yes" : "No"}</td>}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <PerioToothRecords chart={chart} numbering={numbering} />
            <PerioChartLinks chart={chart} canWrite={canWrite} onChanged={onChartChanged} />
            <div className="alv-clinical__row-actions">
              {earlier && (
                <button type="button" className="btn btn-outline-secondary" aria-expanded={comparing === chart.id} onClick={() => setComparing(comparing === chart.id ? null : chart.id)}>
                  {comparing === chart.id ? "Hide the comparison" : "Compare with the previous chart"}
                </button>
              )}
              {canWrite && <button type="button" className="btn btn-outline-secondary" onClick={() => onUseAsStart(chart)}>Start a new chart from these values</button>}
            </div>
            {earlier && comparing === chart.id && (
              <PerioComparisonPanel patientId={patientId} current={{ examId: chart.id }} previousExamId={earlier.id} labels={{ current: when(chart.recordedAtUtc), previous: when(earlier.recordedAtUtc) }} numbering={numbering} />
            )}
          </details>
        );
      })}
    </section>
  );
}
