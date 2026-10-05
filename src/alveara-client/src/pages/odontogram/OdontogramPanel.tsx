import { useCallback, useEffect, useMemo, useState } from "react";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import { clinicalFieldErrorsOf, isNetworkFailure } from "../../services/clinicalApi";
import { getChart, getConditionTypes } from "../../services/odontogramApi";
import type { Chart, ConditionType, Finding } from "../../services/odontogramApi";
import type { PatientDetail } from "../../services/patientsApi";
import type { SaveResult } from "../safety/SafetyForms";
import { ConditionTypesPanel } from "./ConditionTypesPanel";
import { DENTITION_VIEWS, keysInView } from "./dentition";
import type { DentitionView } from "./dentition";
import { ToothChart } from "./ToothChart";
import { ToothDetail } from "./ToothDetail";
import { ALL_STATES, STATE_MEANING, findingsByTooth, plural, stateClass } from "./odontogramText";
import { DEFAULT_NUMBERING, SURFACE_NAMES, displayTooth, isPrimary, toothName } from "./toothNumbering";
import type { NumberingSystem } from "./toothNumbering";
import "./Odontogram.css";

type Saving = { kind: "idle" } | { kind: "saving" } | { kind: "saved"; text: string } | { kind: "failed"; text: string };

interface Props {
  patient: PatientDetail;
  /** The numbering system the chart shows. A practice-level preference for it is not built yet, so this defaults to Universal. */
  numbering?: NumberingSystem;
  /** Whether the signed-in person may change the chart. It only decides which controls are drawn (the server checks every call) and never decides when data is loaded. */
  canWrite?: boolean;
  /** Whether the signed-in person may change the condition catalogue (add, retire, reactivate). Also only decides which controls are drawn. */
  canManageConditions?: boolean;
}

const VIEW_TEXT: Record<DentitionView, string> = { Permanent: "Permanent teeth", Primary: "Primary teeth", Mixed: "Mixed (both)" };

/**
 * STORY-006 / ALV-006-C01: the patient's odontogram, for permanent, primary or mixed dentition. The chart shows each tooth with the state of what is recorded on it, written as words (the
 * border style repeats it, never alone). Selecting a tooth shows what is recorded on it by surface, its whole history and, for people who may change the chart, lets them record a finding,
 * move it forward (Diagnosed, Planned, Completed) or withdraw a wrong entry with a reason. The conditions a finding can be are the practice's catalogue, which people who manage clinical
 * templates can extend. The chart only ever shows what is recorded: an empty chart says so and never implies healthy teeth, and a failed load says so rather than showing an empty chart.
 *
 * <b>Permanent, primary and mixed are three VIEWS of the same findings.</b> Switching never changes, hides or loses anything recorded (findings are keyed by tooth, not by view); the chart
 * starts as Mixed when the patient already has a finding on a primary tooth and as Permanent otherwise. Findings on teeth the current view does not draw are listed under the chart.
 *
 * Every change is sent as it is made and the screen is replaced by what the server returns; the status line says in words whether the last change was saved, is saving or failed. A stale
 * change shows the shared conflict banner and a reload that refreshes in place, so an open form keeps what was typed. A dropped connection keeps what was typed too.
 */
export function OdontogramPanel({ patient, numbering = DEFAULT_NUMBERING, canWrite = false, canManageConditions = false }: Props) {
  const [chart, setChart] = useState<Chart | null>(null);
  const [types, setTypes] = useState<ConditionType[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const [surface, setSurface] = useState<string | null>(null);
  const [view, setView] = useState<DentitionView | null>(null);
  const [saving, setSaving] = useState<Saving>({ kind: "idle" });
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);

  const adoptChart = useCallback((next: Chart) => {
    setChart(next);
    setConflict(null);
  }, []);
  const adoptTypes = useCallback((next: ConditionType[]) => {
    setTypes(next);
    setConflict(null);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    getChart(patient.id, controller.signal).then(adoptChart).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    getConditionTypes(controller.signal).then(adoptTypes).catch(() => { /* the catalogue not loading is said where it is needed; the chart still shows */ });
    return () => controller.abort();
  }, [patient.id, adoptChart, adoptTypes]);

  const findings = useMemo(() => chart?.findings ?? [], [chart]);
  const byTooth = useMemo(() => findingsByTooth(findings), [findings]);
  const effectiveView: DentitionView = view ?? (findings.some((f) => isPrimary(f.toothKey)) ? "Mixed" : "Permanent");
  const drawn = useMemo(() => keysInView(effectiveView), [effectiveView]);
  const notDrawn: Finding[] = findings.filter((f) => !drawn.has(f.toothKey));
  const busy = saving.kind === "saving";

  async function exec<T>(change: () => Promise<T>, adopt: (value: T) => void, savedText: string, entity: string): Promise<SaveResult> {
    setSaving({ kind: "saving" });
    try {
      adopt(await change());
      setSaving({ kind: "saved", text: savedText });
      return null;
    } catch (err) {
      if (isConcurrencyConflict(err)) {
        setConflict({ ...err.body, entityType: entity });                          // the shared banner prints the entity name as given, so give it words, not "ToothFinding"
        setSaving({ kind: "failed", text: "Not saved: someone else changed this." });
      } else if (isNetworkFailure(err)) {
        setSaving({ kind: "failed", text: "Not saved: the connection dropped. What you typed is still here; try again." });
      } else {
        setSaving({ kind: "failed", text: `Not saved: ${err instanceof ApiError ? err.message : "something went wrong."}` });
      }
      return clinicalFieldErrorsOf(err);
    }
  }
  const run = (change: () => Promise<Chart>, savedText: string) => exec(change, adoptChart, savedText, "tooth finding");
  const runTypes = (change: () => Promise<ConditionType[]>, savedText: string) => exec(change, adoptTypes, savedText, "condition type");

  async function reload() {
    try {
      const [nextChart, nextTypes] = await Promise.all([getChart(patient.id), getConditionTypes()]);
      adoptChart(nextChart);
      adoptTypes(nextTypes);
      setSaving({ kind: "idle" });
    } catch {
      setSaving({ kind: "failed", text: "Could not reload. Check your connection and try again." });
    }
  }

  if (failed) return <ErrorState title="Could not load the odontogram" description="Do not assume nothing is recorded. Check your connection and reload the page." />;
  if (chart === null) return <LoadingState label="Loading the odontogram…" />;

  return (
    <section className="alv-clinical alv-odonto" aria-labelledby="alv-odonto-title">
      <h2 id="alv-odonto-title" className="alv-workspace__section-title">Odontogram</h2>
      <p className="alv-clinical__meta">
        Teeth numbered with the {numbering === "Fdi" ? "FDI" : numbering} system. The word under each number is the state of what is recorded on that tooth. An empty tooth means
        nothing is recorded, not that it is healthy.
      </p>
      <p className="alv-clinical__status" role="status" aria-live="polite">
        {saving.kind === "saving" ? "Saving…" : saving.kind === "idle" ? (canWrite ? "Every change is saved as you make it." : "You can read this but your role cannot change it.") : saving.text}
      </p>
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => void reload()} />}

      <div className="alv-odonto__views" role="group" aria-label="Teeth shown">
        {DENTITION_VIEWS.map((v) => (
          <button key={v} type="button" className={`alv-odonto__view${effectiveView === v ? " alv-odonto__view--selected" : ""}`} aria-pressed={effectiveView === v} onClick={() => setView(v)}>{VIEW_TEXT[v]}</button>
        ))}
      </div>
      <p className="alv-clinical__meta">Choosing a view only changes which teeth are drawn. Nothing recorded is changed, hidden from the history or lost.</p>

      <ul className="alv-odonto__legend" aria-label="Legend">
        {ALL_STATES.map((s) => (
          <li key={s}><span className={`alv-odonto__swatch ${stateClass(s)}`} aria-hidden="true" /> <strong>{s}</strong> <span className="alv-clinical__meta">{STATE_MEANING[s]}</span></li>
        ))}
      </ul>

      {findings.length === 0 && <p className="alv-clinical__meta">No findings are recorded on any tooth.</p>}

      <ToothChart view={effectiveView} byTooth={byTooth} numbering={numbering} selected={selected} onSelect={(k) => { setSelected(k === selected ? null : k); setSurface(null); }} />
      <p className="alv-clinical__meta">Arrow keys move between teeth; Enter or Space selects one.</p>

      {selected ? (
        <ToothDetail
          key={selected}
          patientId={patient.id}
          toothKey={selected}
          numbering={numbering}
          findings={byTooth.get(selected) ?? []}
          conditionTypes={types}
          selectedSurface={surface}
          onSurface={setSurface}
          canWrite={canWrite}
          busy={busy}
          run={run}
        />
      ) : (
        <p className="alv-clinical__meta">Select a tooth to see what is recorded on it.</p>
      )}

      {notDrawn.length > 0 && (
        <section className="alv-clinical__section" aria-labelledby="alv-odonto-undrawn">
          <h3 id="alv-odonto-undrawn" className="alv-clinical__section-title">Recorded on teeth not drawn in this view</h3>
          <p className="alv-clinical__meta">The {VIEW_TEXT[effectiveView].toLowerCase()} view does not draw these teeth. {plural(notDrawn.length, "finding is", "findings are")} recorded on them; choose another view to see them on the chart:</p>
          <ul className="alv-clinical__entries">
            {notDrawn.map((f) => (
              <li key={f.id}>Tooth {displayTooth(f.toothKey, numbering)} ({toothName(f.toothKey)}): {f.conditionLabel}{f.surface ? `, ${SURFACE_NAMES[f.surface]} surface` : ", whole tooth"}: {f.state}</li>
            ))}
          </ul>
        </section>
      )}

      <ConditionTypesPanel types={types} canManage={canManageConditions} busy={busy} run={runTypes} />
    </section>
  );
}
