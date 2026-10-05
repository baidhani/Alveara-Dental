import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { LoadingState } from "../../components/StatePatterns";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import {
  abandonPerioSession, finalizePerioSession, getCurrentPerioSession, getPerioSession, perioProblemsOf, savePerioEntries, startPerioSession,
} from "../../services/perioApi";
import type { PerioChart, PerioProblem, PerioReadingInput, PerioSession, PerioToothInput } from "../../services/perioApi";
import type { PatientDetail } from "../../services/patientsApi";
import { displayTooth } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { PerioComparisonPanel } from "./PerioComparisonPanel";
import { PerioEntryForm } from "./PerioEntryForm";
import type { Measures } from "./PerioEntryForm";
import { PerioToothControls } from "./PerioToothControls";
import { PerioToothStrip } from "./PerioToothStrip";
import { SITES, cellId } from "./perioChartRules";
import {
  bufferedSites, effectiveReadings, effectiveTeeth, emptyBuffer, firstIndexOfTooth, firstOpenIndex, isBufferEmpty, mergeBack, nextOpenIndex, pathFor, skippedTeeth, toBatch,
  withReading, withSiteCleared, withTooth, withToothCleared,
} from "./perioEntryState";
import type { EntryBuffer } from "./perioEntryState";
import { describeSite } from "./perioSiteModel";

type Status = { kind: "idle" } | { kind: "saving" } | { kind: "saved"; text: string } | { kind: "failed"; text: string };

interface Props {
  patient: PatientDetail;
  numbering: NumberingSystem;
  /** Whether the signed-in person may chart. It only decides which controls are drawn (the server checks every call). */
  canWrite: boolean;
  /** Called with the chart a finalized draft became, so the charts on record can show it at once. */
  onFinalized: (chart: PerioChart) => void;
  /** The latest finalized chart, if any: what the draft is compared with. */
  latestChart: PerioChart | null;
}

/**
 * ALV-012-C01: charting a whole mouth step by step. One chart is entered as a DRAFT on the server: the cursor follows the documented entry order (cheek side across the arch, back along the tongue
 * side, then the lower arch), each site is typed and confirmed from the keyboard, and what is typed waits in a buffer that is saved into the draft when the cursor leaves a tooth or on demand. The
 * screen always shows the draft overlaid with that buffer, so nothing typed is ever hidden or lost: if a save is refused the entries are marked and kept and the cursor goes to the first one to
 * correct; if the draft was changed by someone else the shared conflict banner offers a reload that keeps what was typed; a dropped connection keeps it too. Teeth marked not charted, or missing in
 * the odontogram, are skipped without disturbing the order. Finalizing saves what is waiting, then turns the draft into the chart on record.
 */
export function PerioSessionPanel({ patient, numbering, canWrite, onFinalized, latestChart }: Props) {
  const [session, setSessionState] = useState<PerioSession | null | undefined>(undefined);
  const [failed, setFailed] = useState(false);
  const [buffer, setBufferState] = useState<EntryBuffer>(emptyBuffer);
  const [inFlight, setInFlight] = useState<EntryBuffer>(emptyBuffer);
  const [cursor, setCursor] = useState<string | null>(null);
  const [measures, setMeasures] = useState<Measures>({ suppuration: false, plaque: false });
  const [status, setStatus] = useState<Status>({ kind: "idle" });
  const [problems, setProblems] = useState<PerioProblem[]>([]);
  const [conflict, setConflict] = useState(false);
  const [confirmAbandon, setConfirmAbandon] = useState(false);
  const [comparing, setComparing] = useState(false);
  const bufferRef = useRef(buffer);
  const sessionRef = useRef<PerioSession | null | undefined>(undefined);
  const chain = useRef<Promise<void>>(Promise.resolve());

  const setSession = (s: PerioSession | null) => { sessionRef.current = s; setSessionState(s); };
  const setBuffer = (update: (b: EntryBuffer) => EntryBuffer) => { bufferRef.current = update(bufferRef.current); setBufferState(bufferRef.current); };

  useEffect(() => {
    const controller = new AbortController();
    getCurrentPerioSession(patient.id, controller.signal).then((r) => setSession(r.session)).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [patient.id]);

  const readings = useMemo(() => effectiveReadings(session?.readings ?? [], inFlight, buffer), [session, inFlight, buffer]);
  const teeth = useMemo(() => effectiveTeeth(session?.teeth ?? [], inFlight, buffer), [session, inFlight, buffer]);
  const path = useMemo(() => pathFor(skippedTeeth(teeth, session?.absentTeeth ?? [])), [teeth, session]);
  const idx = cursor !== null && path.indexOf.has(cursor) ? path.indexOf.get(cursor)! : firstOpenIndex(path, readings);
  const here = idx >= 0 ? path.order[idx] : null;
  const unsaved = bufferedSites(buffer) + bufferedSites(inFlight) + Object.keys(buffer.teeth).length + Object.keys(inFlight.teeth).length + buffer.clearTeeth.length + inFlight.clearTeeth.length;
  useUnsavedChangesWarning(unsaved > 0);

  const explain = useCallback((err: unknown, doing: string) => {
    if (isConcurrencyConflict(err)) { setConflict(true); setStatus({ kind: "failed", text: `Not ${doing}: someone else changed this chart. What you entered is still here.` }); return; }
    const found = perioProblemsOf(err);
    if (err instanceof ApiError && found.length > 0) {
      setProblems(found);
      const target = found.find((p) => p.toothKey);
      if (target?.toothKey) {
        const key = target.site ? cellId(target.toothKey, target.site) : null;
        setCursor(key && path.indexOf.has(key) ? key : (() => { const i = firstIndexOfTooth(path, target.toothKey!); return i >= 0 ? cellId(path.order[i].toothKey, path.order[i].site) : null; })());
      }
      setStatus({ kind: "failed", text: `Not ${doing}: some entries need correcting. Everything you entered is still here.` });
    } else if (isNetworkFailure(err)) setStatus({ kind: "failed", text: `Not ${doing}: the connection dropped. What you entered is still here; try again.` });
    else setStatus({ kind: "failed", text: `Not ${doing}: ${err instanceof ApiError ? err.message : "something went wrong."} What you entered is still here.` });
  }, [path]);

  async function doFlush() {
    const snapshot = bufferRef.current;
    const current = sessionRef.current;
    if (isBufferEmpty(snapshot) || !current) return;
    setBuffer(() => emptyBuffer());
    setInFlight(snapshot);
    setStatus({ kind: "saving" });
    try {
      setSession(await savePerioEntries(current, toBatch(snapshot)));
      setProblems([]);
      setStatus({ kind: "saved", text: "Saved." });
    } catch (err) {
      setBuffer((b) => mergeBack(snapshot, b));            // what was in flight goes back; anything typed since wins
      explain(err, "saved");
    } finally {
      setInFlight(emptyBuffer());
    }
  }
  const flush = () => { chain.current = chain.current.then(doFlush); return chain.current; };

  function commit(reading: PerioReadingInput) {
    setBuffer((b) => withReading(b, reading));
    const key = cellId(reading.toothKey, reading.site);
    const after = new Map(readings).set(key, reading);
    const next = nextOpenIndex(path, after, idx + 1);
    if (next === null) { setStatus({ kind: "saved", text: "Every site has a reading. Save the chart to finish." }); void flush(); return; }
    setCursor(cellId(path.order[next].toothKey, path.order[next].site));
    if (path.order[next].toothKey !== reading.toothKey) void flush();
  }

  const go = (i: number) => { const s = path.order[Math.max(0, Math.min(path.order.length - 1, i))]; if (s) setCursor(cellId(s.toothKey, s.site)); };
  const jump = (toothKey: string) => { const i = firstIndexOfTooth(path, toothKey); if (i >= 0) { go(i); void flush(); } };

  function skipTooth() {
    if (!here) return;
    const tooth = here.toothKey;
    for (const s of SITES) if (readings.has(cellId(tooth, s))) setBuffer((b) => withSiteCleared(b, tooth, s));
    setBuffer((b) => withTooth(b, { toothKey: tooth, mobility: null, furcation: null, excluded: true }));
    const nextTooth = path.order.findIndex((s, i) => i > idx && s.toothKey !== tooth);
    setCursor(nextTooth >= 0 ? cellId(path.order[nextTooth].toothKey, path.order[nextTooth].site) : null);
    void flush();
  }

  const includeTooth = (toothKey: string) => { setBuffer((b) => withToothCleared(b, toothKey)); void flush(); };
  const setToothRecord = (record: PerioToothInput) => setBuffer((b) => withTooth(b, record));

  async function start() {
    try { setSession(await startPerioSession(patient.id)); setStatus({ kind: "idle" }); } catch (err) { explain(err, "started"); }
  }

  async function reload() {
    if (!session) return;
    try {
      const fresh = await getPerioSession(session.id);
      setSession(fresh.status === "Draft" ? fresh : null);
      setConflict(false);
      setStatus({ kind: "idle" });
      if (fresh.status !== "Draft") setStatus({ kind: "failed", text: `This chart was ${fresh.status.toLowerCase()} by someone else. What you entered could not be added to it.` });
    } catch { setStatus({ kind: "failed", text: "Could not reload. Check your connection and try again." }); }
  }

  async function finalize() {
    await flush();
    if (!isBufferEmpty(bufferRef.current) || !sessionRef.current) return;
    setStatus({ kind: "saving" });
    try {
      const chart = await finalizePerioSession(sessionRef.current);
      onFinalized(chart);
      setSession(null);
      setBuffer(() => emptyBuffer());
      setProblems([]);
      setStatus({ kind: "saved", text: `Chart saved with ${chart.readingCount} ${chart.readingCount === 1 ? "site" : "sites"}. It is now in the charts on record.` });
    } catch (err) { explain(err, "finalized"); }
  }

  async function abandon() {
    if (!session) return;
    setConfirmAbandon(false);
    try {
      await abandonPerioSession(session);
      setSession(null);
      setBuffer(() => emptyBuffer());
      setProblems([]);
      setStatus({ kind: "saved", text: "The chart in progress was discarded." });
    } catch (err) { explain(err, "discarded"); }
  }

  if (failed) return <p className="alv-clinical__meta" role="alert">Could not load the chart in progress. Do not assume none is open. Reload the page to try again.</p>;
  if (session === undefined) return <LoadingState label="Loading the chart in progress…" />;

  const entered = path.order.filter((s) => readings.has(cellId(s.toothKey, s.site))).length;
  const statusText = status.kind === "saving" ? "Saving…" : status.kind === "idle" || status.kind === "saved" && status.text === "Saved."
    ? `${entered} of ${path.order.length} sites entered${unsaved > 0 ? `; ${unsaved} not saved yet` : session ? "; all saved" : ""}.` : status.text;

  if (session === null) {
    return (
      <section className="alv-perio__steps" aria-labelledby="alv-perio-steps-title">
        <h3 id="alv-perio-steps-title">Step-by-step chart</h3>
        <p className="alv-perio__sessionstatus" role="status">{status.kind === "idle" ? "No chart is in progress." : status.kind === "failed" || status.kind === "saved" ? status.text : ""}</p>
        {canWrite
          ? <button type="button" className="btn btn-primary" onClick={() => void start()}>Start a step-by-step chart</button>
          : <p className="alv-clinical__meta">Your role can read charts but cannot start one.</p>}
      </section>
    );
  }

  const absent = session.absentTeeth.map((k) => displayTooth(k, numbering)).join(", ");
  return (
    <section className="alv-perio__steps" aria-labelledby="alv-perio-steps-title">
      <h3 id="alv-perio-steps-title">Step-by-step chart</h3>
      <p className="alv-clinical__meta">
        Started by {session.startedByName}. Type the depth and press Enter, then the recession (0 if none) and press Enter to go on. B toggles bleeding{measures.suppuration ? ", S pus" : ""}{measures.plaque ? ", P plaque" : ""}; X marks
        the tooth as not charted; Shift+Enter goes back. The order is the cheek side across the arch, then back along the tongue side, then the lower arch.
      </p>
      <p className="alv-perio__sessionstatus" role="status" aria-live="polite">{statusText}</p>
      {conflict && <ConcurrencyConflictBanner problem={{ error: "concurrency_conflict", entityType: "chart in progress", entityId: session.id }} onReload={() => void reload()} />}
      {problems.length > 0 && (
        <div className="alv-perio__problems" role="alert">
          <h4>Some entries need correcting</h4>
          <ul>{problems.map((p, i) => <li key={`${p.toothKey}${p.site}${p.field}${i}`}>{p.toothKey && <strong>Tooth {displayTooth(p.toothKey, numbering)}{p.site ? `, ${describeSite(p.toothKey, p.site)}` : ""}: </strong>}{p.message}</li>)}</ul>
          <p className="alv-clinical__meta">Tooth numbers in these messages from the server are FDI numbers.</p>
        </div>
      )}
      {absent && <p className="alv-clinical__meta">Missing in the odontogram, so skipped: tooth {absent}.</p>}

      {canWrite ? (
        <>
          <fieldset className="alv-perio__measures">
            <legend>Also record at each site</legend>
            <label><input type="checkbox" checked={measures.suppuration} onChange={(e) => setMeasures((m) => ({ ...m, suppuration: e.target.checked }))} /> Pus (suppuration)</label>
            <label><input type="checkbox" checked={measures.plaque} onChange={(e) => setMeasures((m) => ({ ...m, plaque: e.target.checked }))} /> Plaque</label>
            <span className="alv-clinical__meta">A measure left unticked is saved as not assessed, not as none.</span>
          </fieldset>
          <PerioToothStrip numbering={numbering} readings={readings} teeth={teeth} absent={session.absentTeeth} current={here?.toothKey ?? null} onJump={jump} onInclude={includeTooth} />
          {here
            ? (
              <>
                <PerioEntryForm key={`${here.toothKey}:${here.site}`} toothKey={here.toothKey} site={here.site} numbering={numbering} existing={readings.get(cellId(here.toothKey, here.site))}
                  measures={measures} position={{ number: idx + 1, total: path.order.length }} onCommit={commit} onBack={() => go(idx - 1)}
                  onClear={() => setBuffer((b) => withSiteCleared(b, here.toothKey, here.site))} onSkipTooth={skipTooth} />
                <PerioToothControls key={here.toothKey} toothKey={here.toothKey} numbering={numbering} record={teeth.get(here.toothKey)} onChange={setToothRecord} />
              </>
            )
            : <p className="alv-clinical__meta">Every tooth is skipped, so there is nothing to chart. Put a tooth back to continue.</p>}
          <div className="alv-clinical__row-actions">
            <button type="button" className="btn btn-outline-secondary" disabled={unsaved === 0 || status.kind === "saving"} onClick={() => void flush()}>Save what I have entered</button>
            <button type="button" className="btn btn-primary" disabled={entered === 0 || status.kind === "saving"} onClick={() => void finalize()}>Finish and save the chart</button>
            <button type="button" className="btn btn-outline-secondary" onClick={() => setConfirmAbandon(true)}>Discard this chart</button>
          </div>
          {latestChart && (
            <div>
              <button type="button" className="btn btn-outline-secondary" aria-expanded={comparing} onClick={() => setComparing((v) => !v)}>
                {comparing ? "Hide the comparison" : "Compare with the last finalized chart"}
              </button>
              {comparing && (
                <>
                  <p className="alv-clinical__meta">This compares the sites already saved into the draft{unsaved > 0 ? `; ${unsaved} entered but not saved yet ${unsaved === 1 ? "is" : "are"} not in it. Save what you have entered to include them` : ""}.</p>
                  <PerioComparisonPanel key={session.rowVersion} patientId={patient.id} current={{ sessionId: session.id }} previousExamId={latestChart.id}
                    labels={{ current: "The chart in progress", previous: new Date(latestChart.recordedAtUtc).toLocaleString() }} numbering={numbering} />
                </>
              )}
            </div>
          )}
          {confirmAbandon && (
            <div className="alv-perio__problems" role="alertdialog" aria-label="Discard this chart?">
              <p>Discarding closes this chart in progress for good; nothing in it becomes a chart on record. Entries already saved stay in the record of what was done.</p>
              <div className="alv-clinical__row-actions">
                <button type="button" className="btn btn-primary" onClick={() => void abandon()}>Discard</button>
                <button type="button" className="btn btn-outline-secondary" onClick={() => setConfirmAbandon(false)}>Keep charting</button>
              </div>
            </div>
          )}
        </>
      ) : <p className="alv-clinical__meta">{entered} of {path.order.length} sites entered so far. Your role can read this but cannot change it.</p>}
    </section>
  );
}
