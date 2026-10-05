import { useEffect, useMemo, useState } from "react";
import { LoadingState } from "../../components/StatePatterns";
import { ApiError } from "../../services/authApi";
import { comparePerio } from "../../services/perioApi";
import type { PerioComparison, PerioFigures, PerioSiteChange } from "../../services/perioApi";
import { displayTooth } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { CUE_TEXT, DEEP_MM, SIGNIFICANT_CHANGE_MM, VERY_DEEP_MM, describeSite } from "./perioSiteModel";
import type { Cue } from "./perioSiteModel";

interface Props {
  patientId: string;
  /** The chart being looked at: a saved chart, or the draft being entered (its saved sites; what is still waiting to be saved is not in it). */
  current: { examId: string } | { sessionId: string };
  /** Defaults to the latest finalized chart before the current one. */
  previousExamId?: string;
  /** How the two charts are named in words, for example a date. */
  labels: { current: string; previous: string };
  numbering: NumberingSystem;
}

type Load = { kind: "loading" } | { kind: "failed" } | { kind: "none"; message: string } | { kind: "ready"; data: PerioComparison };

const signed = (n: number, digits = 0) => `${n > 0 ? "+" : ""}${n.toFixed(digits)}`;
const yesNo = (v: boolean | null) => (v === null ? "no reading" : v ? "Yes" : "No");
const TREND_TEXT: Record<string, string> = { Improved: "Improved", Worsened: "Worse", Unchanged: "About the same", OnlyPrevious: "Only in the earlier chart", OnlyCurrent: "Only in this chart" };

/** The whole-chart figures as rows: what each measure is, then and now, and the plain difference. No row says better or worse; the counts of sites that improved or worsened follow the stated 2 mm rule. */
function figureRows(p: PerioFigures, c: PerioFigures): { measure: string; before: string; now: string; change: string }[] {
  const n = (a: number, b: number) => ({ before: String(a), now: String(b), change: signed(b - a) });
  const d = (a: number, b: number) => ({ before: a.toFixed(1), now: b.toFixed(1), change: signed(b - a, 1) });
  return [
    { measure: "Sites charted", ...n(p.sites, c.sites) },
    { measure: "Mean probing depth (mm)", ...d(p.meanDepthMm, c.meanDepthMm) },
    { measure: "Mean attachment loss (mm)", ...d(p.meanAttachmentLossMm, c.meanAttachmentLossMm) },
    { measure: "Sites that bled on probing (%)", ...n(p.bleedingPercent, c.bleedingPercent) },
    { measure: `Sites ${DEEP_MM} mm or deeper`, ...n(p.deepSites, c.deepSites) },
    { measure: `Sites ${VERY_DEEP_MM} mm or deeper`, ...n(p.veryDeepSites, c.veryDeepSites) },
    { measure: "Sites with pus", ...n(p.suppurationSites, c.suppurationSites) },
    p.plaquePercent === null || c.plaquePercent === null
      ? { measure: "Sites with plaque (% of those assessed)", before: p.plaquePercent === null ? "not assessed" : `${p.plaquePercent}`, now: c.plaquePercent === null ? "not assessed" : `${c.plaquePercent}`, change: "-" }
      : { measure: "Sites with plaque (% of those assessed)", ...n(p.plaquePercent, c.plaquePercent) },
  ];
}

/** Says only what is true: which side the sites charted once were on, and that they are not in the counts above. */
function unmatchedSentence(onlyPrevious: number, onlyCurrent: number): string {
  const sites = (n: number) => `${n} ${n === 1 ? "site" : "sites"}`;
  const both = onlyPrevious > 0 && onlyCurrent > 0;
  const where = both ? `${sites(onlyPrevious)} ${onlyPrevious === 1 ? "was" : "were"} charted only in the earlier chart and ${onlyCurrent} only in this one`
    : onlyPrevious > 0 ? `${sites(onlyPrevious)} ${onlyPrevious === 1 ? "was" : "were"} charted only in the earlier chart` : `${sites(onlyCurrent)} ${onlyCurrent === 1 ? "was" : "were"} charted only in this chart`;
  return `${where}, so ${onlyPrevious + onlyCurrent === 1 ? "it is" : "they are"} not counted above.`;
}

const changed = (s: PerioSiteChange) => s.trend !== "Unchanged" || s.previousBleeding !== s.currentBleeding;

/**
 * ALV-012-C01: a chart set beside an earlier one, with the trend between them. Every number is arithmetic on the two charts' readings, done on the server and shown here as it came. Sites charted both times
 * are counted as improved or worse only when the probing depth moved by 2 mm or more (a smaller move is within the error of measuring); sites in only one chart are listed and counted separately, never
 * dropped. The cue words (deeper pocket, recession, bleeding, pus) draw the eye and are not a diagnosis. A failed load says so; no earlier chart says that; neither is shown as "no change".
 */
export function PerioComparisonPanel({ patientId, current, previousExamId, labels, numbering }: Props) {
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [showAll, setShowAll] = useState(false);
  const key = "examId" in current ? `e:${current.examId}` : `s:${current.sessionId}`;

  useEffect(() => {
    const controller = new AbortController();
    comparePerio(patientId, current, previousExamId, controller.signal)
      .then((data) => setLoad({ kind: "ready", data }))
      .catch((err) => {
        if (controller.signal.aborted) return;
        setLoad(err instanceof ApiError && err.code === "no_previous_chart" ? { kind: "none", message: err.message } : { kind: "failed" });
      });
    return () => controller.abort();
    // `current` is represented by `key`; re-reading is wanted only when what is being compared changes
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [patientId, key, previousExamId]);

  const rows = useMemo(() => (load.kind === "ready" ? load.data.sites.filter((s) => showAll || changed(s)) : []), [load, showAll]);

  if (load.kind === "loading") return <LoadingState label="Loading the comparison…" />;
  if (load.kind === "failed") return <p className="alv-clinical__meta" role="alert">Could not load the comparison. Do not assume nothing changed. Check your connection and try again.</p>;
  if (load.kind === "none") return <p className="alv-clinical__meta" role="status">{load.message}</p>;

  const c = load.data;
  return (
    <section className="alv-perio__compare" aria-label={`Comparison: ${labels.current} with ${labels.previous}`}>
      <h4>{labels.current} compared with {labels.previous}</h4>
      <p className="alv-perio__trend" role="status">
        Of {c.matchedSites} {c.matchedSites === 1 ? "site" : "sites"} charted both times, <strong>{c.improved}</strong> improved ({SIGNIFICANT_CHANGE_MM} mm or more shallower), <strong>{c.worsened}</strong> got worse ({SIGNIFICANT_CHANGE_MM} mm or more deeper) and <strong>{c.unchanged}</strong> are about the same.
        {" "}The mean change in probing depth at those sites is {signed(c.meanDepthChangeMm, 1)} mm.
        {(c.onlyPrevious > 0 || c.onlyCurrent > 0) && <> {unmatchedSentence(c.onlyPrevious, c.onlyCurrent)}</>}
      </p>
      <div className="alv-perio__arch">
        <table className="alv-perio__table">
          <caption className="alv-perio__sr">Whole-chart figures, {labels.previous} and {labels.current}</caption>
          <thead><tr><th scope="col">Measure</th><th scope="col">{labels.previous}</th><th scope="col">{labels.current}</th><th scope="col">Change</th></tr></thead>
          <tbody>{figureRows(c.previous, c.current).map((r) => <tr key={r.measure}><th scope="row">{r.measure}</th><td>{r.before}</td><td>{r.now}</td><td>{r.change}</td></tr>)}</tbody>
        </table>
      </div>

      <label className="alv-perio__showall"><input type="checkbox" checked={showAll} onChange={(e) => setShowAll(e.target.checked)} /> Show every site, including those that did not change</label>
      {rows.length === 0
        ? <p className="alv-clinical__meta">{showAll ? "There are no sites to show." : "No site changed between these charts."}</p>
        : (
          <div className="alv-perio__arch">
            <table className="alv-perio__table">
              <caption className="alv-perio__sr">Site by site, {labels.previous} and {labels.current}</caption>
              <thead>
                <tr><th scope="col">Tooth</th><th scope="col">Site</th><th scope="col">Depth then (mm)</th><th scope="col">Depth now (mm)</th><th scope="col">Change (mm)</th><th scope="col">Attachment loss then to now (mm)</th><th scope="col">Bleeding then to now</th><th scope="col">Result</th><th scope="col">Worth a second look now</th></tr>
              </thead>
              <tbody>
                {rows.map((s) => (
                  <tr key={`${s.toothKey}${s.site}`}>
                    <th scope="row">{displayTooth(s.toothKey, numbering)}</th>
                    <td>{describeSite(s.toothKey, s.site)}</td>
                    <td>{s.previousDepthMm ?? "-"}</td><td>{s.currentDepthMm ?? "-"}</td><td>{s.depthChangeMm === null ? "-" : signed(s.depthChangeMm)}</td>
                    <td>{s.previousAttachmentLossMm ?? "-"} to {s.currentAttachmentLossMm ?? "-"}</td>
                    <td>{yesNo(s.previousBleeding)} to {yesNo(s.currentBleeding)}</td>
                    <td>{TREND_TEXT[s.trend]}</td>
                    <td>{s.currentCues.length === 0 ? "-" : s.currentCues.map((q) => CUE_TEXT[q as Cue] ?? q).join(", ")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      {c.teeth.length > 0 && (
        <div>
          <h5>Teeth whose state or grades changed</h5>
          <ul>
            {c.teeth.map((t) => (
              <li key={t.toothKey}>
                <strong>Tooth {displayTooth(t.toothKey, numbering)}:</strong>{" "}
                {t.previousState !== t.currentState ? `${t.previousState.replace("NotRecorded", "not recorded").toLowerCase()} then, ${t.currentState.replace("NotRecorded", "not recorded").toLowerCase()} now` : "charted both times"}
                {t.previousMobility !== t.currentMobility && `; mobility ${t.previousMobility ?? "not assessed"} then, ${t.currentMobility ?? "not assessed"} now`}
                {t.previousFurcation !== t.currentFurcation && `; furcation ${t.previousFurcation ?? "not assessed"} then, ${t.currentFurcation ?? "not assessed"} now`}.
              </li>
            ))}
          </ul>
        </div>
      )}
      <p className="alv-clinical__meta">These figures and marks are arithmetic on what was recorded and draw the eye to values worth a second look. They are not a diagnosis and do not replace the clinician's judgement.</p>
    </section>
  );
}
