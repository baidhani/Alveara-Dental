import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ApiError } from "../../services/authApi";
import { isNetworkFailure, newKey } from "../../services/clinicalApi";
import { getPerioCharts, perioProblemsOf, savePerioChart } from "../../services/perioApi";
import type { PerioChart, PerioProblem } from "../../services/perioApi";
import type { PatientDetail } from "../../services/patientsApi";
import { DEFAULT_NUMBERING, LOWER_PERMANENT, UPPER_PERMANENT, displayTooth } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { PerioGrid } from "./PerioGrid";
import { PerioHistory } from "./PerioHistory";
import { EMPTY_CELL, SITE_NAMES, cellId, isTouched, readingsFromDraft, signatureOf } from "./perioChartRules";
import type { CellDraft, CellProblem, Draft, Site } from "./perioChartRules";
import { draftFromChart } from "./perioSummary";
import "./Perio.css";

const TEETH = [...UPPER_PERMANENT, ...LOWER_PERMANENT];
const FIELD_OF: Record<string, "pd" | "rec"> = { probingDepthMm: "pd", recessionMm: "rec" };

type Saving = { kind: "idle" } | { kind: "saving" } | { kind: "saved"; text: string } | { kind: "failed"; text: string };

interface Props {
  patient: PatientDetail;
  numbering?: NumberingSystem;
  /** Whether the signed-in person may save a chart. It only decides which controls are drawn (the server checks every save). */
  canWrite?: boolean;
}

/** The server's problems as the cells they belong to; entries about no particular cell (a missing list, a bad key) stay in the summary only. */
function cellProblemsOf(server: PerioProblem[]): CellProblem[] {
  return server.flatMap((p) => {
    const field = FIELD_OF[p.field];
    return field && p.toothKey && p.site ? [{ toothKey: p.toothKey, site: p.site, field, message: p.message }] : [];
  });
}

/**
 * STORY-012: periodontal charting. The dentist (or hygienist) enters probing depth, recession and bleeding for each site of each tooth and saves the chart. Entries are checked as the chart
 * is saved with the same rules the server applies: whole millimetres from 0 to 15, a recession (0 if none) wherever a depth is entered, nothing partial. Anything wrong is listed in one
 * place, each entry marked and linked to its box, and nothing is sent until it is put right; if the server refuses it says what to correct in the same way. A chart that is saved is never edited:
 * changing a value and saving again records a new chart. Each save carries a key, kept across retries of an unchanged chart (a dropped connection or a double click cannot create two) and renewed
 * when anything changes. A failed save says so in words and keeps everything typed.
 */
export function PerioPanel({ patient, numbering = DEFAULT_NUMBERING, canWrite = false }: Props) {
  const [draft, setDraft] = useState<Draft>({});
  const [problems, setProblems] = useState<CellProblem[]>([]);
  const [general, setGeneral] = useState<string[]>([]);
  const [saving, setSaving] = useState<Saving>({ kind: "idle" });
  const [charts, setCharts] = useState<PerioChart[] | null>(null);
  const [historyFailed, setHistoryFailed] = useState(false);
  const [pendingStart, setPendingStart] = useState<PerioChart | null>(null);
  const [serverNote, setServerNote] = useState(false);
  const loadHistory = useCallback((signal?: AbortSignal) => {
    getPerioCharts(patient.id, signal).then((h) => setCharts(h.exams)).catch(() => { if (!signal?.aborted) setHistoryFailed(true); });
  }, [patient.id]);
  useEffect(() => {
    const controller = new AbortController();
    loadHistory(controller.signal);
    return () => controller.abort();
  }, [loadHistory]);

  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const summary = useRef<HTMLDivElement>(null);

  const byCell = useMemo(() => {
    const map = new Map<string, CellProblem[]>();
    for (const p of problems) map.set(cellId(p.toothKey, p.site), [...(map.get(cellId(p.toothKey, p.site)) ?? []), p]);
    return map;
  }, [problems]);
  const entered = Object.values(draft).filter(isTouched).length;

  function change(id: string, patch: Partial<CellDraft>) {
    setDraft((d) => ({ ...d, [id]: { ...EMPTY_CELL, ...d[id], ...patch } }));
    setSaving((s) => (s.kind === "saved" ? { kind: "idle" } : s));
  }

  function refuse(cells: CellProblem[], other: string[], fromServer = false) {
    setProblems(cells);
    setGeneral(other);
    setServerNote(fromServer);
    setSaving({ kind: "failed", text: "Not saved: some entries need correcting. Nothing was sent." });
    setTimeout(() => summary.current?.focus(), 0);
  }

  async function save() {
    setProblems([]);
    setGeneral([]);
    setServerNote(false);
    const { readings, problems: found } = readingsFromDraft(draft, TEETH);
    if (found.length > 0) return refuse(found, []);
    if (readings.length === 0) return refuse([], ["Enter at least one site before saving."]);
    const signature = signatureOf(readings);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setSaving({ kind: "saving" });
    try {
      const chart = await savePerioChart(patient.id, attempt.current.key, readings);
      setCharts((all) => [chart, ...(all ?? []).filter((c) => c.id !== chart.id)]);        // an unchanged retry returns the chart already shown, so it is never listed twice
      setSaving({ kind: "saved", text: `Chart saved with ${chart.readingCount} ${chart.readingCount === 1 ? "site" : "sites"}. Changing a value and saving again records a new chart.` });
    } catch (err) {
      const server = perioProblemsOf(err);
      if (err instanceof ApiError && server.length > 0) {
        refuse(cellProblemsOf(server), server.map((p) => p.message), true);
      } else if (isNetworkFailure(err)) {
        setSaving({ kind: "failed", text: "Not saved: the connection dropped. What you entered is still here; save again to retry." });
      } else {
        setSaving({ kind: "failed", text: `Not saved: ${err instanceof ApiError ? err.message : "something went wrong."}` });
      }
    }
  }

  function startFrom(chart: PerioChart) {
    setDraft(draftFromChart(chart));
    setProblems([]);
    setGeneral([]);
    setPendingStart(null);
    attempt.current = null;
    setSaving({ kind: "idle" });
    document.getElementById("alv-perio-title")?.scrollIntoView?.();
  }

  function focusCell(p: CellProblem) {
    document.querySelector<HTMLInputElement>(`[data-cell="${cellId(p.toothKey, p.site)}"][data-field="${p.field}"]`)?.focus();
  }

  const note = problems.length > 0 || general.length > 0;
  return (
    <section className="alv-clinical alv-perio" aria-labelledby="alv-perio-title">
      <h2 id="alv-perio-title" className="alv-workspace__section-title">Periodontal chart</h2>
      <p className="alv-clinical__meta">
        Teeth numbered with the {numbering === "Fdi" ? "FDI" : numbering} system. For each site enter the probing depth and the recession in whole millimetres (0 to 15) and tick
        the box if it bled on probing. Leave a site completely empty to leave it out. Press Enter to move to the next box.
      </p>
      <p className="alv-clinical__status" role="status" aria-live="polite">
        {saving.kind === "saving" ? "Saving…" : saving.kind === "idle" ? (canWrite ? `${entered} ${entered === 1 ? "site" : "sites"} entered. Nothing is saved until you save the chart.` : "You can read this but your role cannot chart.") : saving.text}
      </p>

      {note && (
        <div className="alv-perio__problems" role="alert" tabIndex={-1} ref={summary}>
          <h3>Some entries need correcting</h3>
          <ul>
            {problems.map((p) => (
              <li key={`${p.toothKey}-${p.site}-${p.field}`}>
                <button type="button" className="alv-perio__jump" onClick={() => focusCell(p)}>
                  Tooth {displayTooth(p.toothKey, numbering)}, {SITE_NAMES[p.site as Site] ?? p.site}
                </button>
                : {p.message}
              </li>
            ))}
            {general.map((m) => <li key={m}>{m}</li>)}
          </ul>
          {serverNote && <p className="alv-clinical__meta">Tooth numbers in the messages from the server are FDI numbers (the first digit is the quadrant, the second the tooth from the middle).</p>}
        </div>
      )}

      <PerioGrid draft={draft} problems={byCell} numbering={numbering} disabled={!canWrite} onChange={change} />

      {canWrite && (
        <div className="alv-clinical__row-actions">
          <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={saving.kind === "saving"}>Save chart</button>
        </div>
      )}

      {pendingStart && (
        <div className="alv-perio__problems" role="alertdialog" aria-label="Replace what you have entered?">
          <p>Starting from this chart replaces the {entered} {entered === 1 ? "site" : "sites"} you have entered and not saved.</p>
          <div className="alv-clinical__row-actions">
            <button type="button" className="btn btn-primary" onClick={() => startFrom(pendingStart)}>Replace</button>
            <button type="button" className="btn btn-outline-secondary" onClick={() => setPendingStart(null)}>Keep what I entered</button>
          </div>
        </div>
      )}
      <PerioHistory charts={charts} failed={historyFailed} numbering={numbering} canWrite={canWrite} onRetry={() => { setHistoryFailed(false); loadHistory(); }} onUseAsStart={(c) => (entered > 0 ? setPendingStart(c) : startFrom(c))} />
    </section>
  );
}
