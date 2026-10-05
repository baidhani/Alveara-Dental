/** STORY-012: the figures a dentist reads off a saved chart. Plain arithmetic on the readings, so they can be checked by eye against the table beneath them. */
import type { PerioChart } from "../../services/perioApi";
import { cellId } from "./perioChartRules";
import type { Draft } from "./perioChartRules";

/** A site this deep (probing depth, mm) or deeper is counted as a deeper pocket. */
export const DEEP_POCKET_MM = 4;

export interface ChartSummary {
  sites: number;
  teeth: number;
  /** Sites that bled on probing, as a whole-number percentage of the sites charted. */
  bleedingPercent: number;
  deepSites: number;
  deepestMm: number;
}

export function summarize(chart: PerioChart): ChartSummary {
  const r = chart.readings;
  const bleeding = r.filter((x) => x.bleeding).length;
  return {
    sites: r.length,
    teeth: new Set(r.map((x) => x.toothKey)).size,
    bleedingPercent: r.length === 0 ? 0 : Math.round((100 * bleeding) / r.length),
    deepSites: r.filter((x) => x.probingDepthMm >= DEEP_POCKET_MM).length,
    deepestMm: r.reduce((m, x) => Math.max(m, x.probingDepthMm), 0),
  };
}

/** A saved chart as the grid's text boxes, so a new chart can start from the last one. */
export function draftFromChart(chart: PerioChart): Draft {
  return Object.fromEntries(chart.readings.map((x) => [cellId(x.toothKey, x.site), { pd: String(x.probingDepthMm), rec: String(x.recessionMm), bleeding: x.bleeding }]));
}
