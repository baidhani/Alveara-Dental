import { useCallback, useEffect, useMemo, useState } from "react";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import { clinicalFieldErrorsOf, isNetworkFailure } from "../../services/clinicalApi";
import { CONDITION_LABELS, getChart } from "../../services/odontogramApi";
import type { Chart, Finding } from "../../services/odontogramApi";
import type { PatientDetail } from "../../services/patientsApi";
import type { SaveResult } from "../safety/SafetyForms";
import { ToothChart } from "./ToothChart";
import { ToothDetail } from "./ToothDetail";
import { ALL_STATES, STATE_MEANING, findingsByTooth, plural, stateClass } from "./odontogramText";
import { DEFAULT_NUMBERING, UPPER_PERMANENT, LOWER_PERMANENT, SURFACE_NAMES, displayTooth, toothName } from "./toothNumbering";
import type { NumberingSystem } from "./toothNumbering";
import "./Odontogram.css";

type Saving = { kind: "idle" } | { kind: "saving" } | { kind: "saved"; text: string } | { kind: "failed"; text: string };

interface Props {
  patient: PatientDetail;
  /** The numbering system the chart shows. A practice-level preference for it is not built yet, so this defaults to Universal. */
  numbering?: NumberingSystem;
  /** Whether the signed-in person may change the chart. It only decides which controls are drawn (the server checks every call) and never decides when data is loaded. */
  canWrite?: boolean;
}

/**
 * STORY-006: the patient's odontogram. The chart shows the 32 permanent teeth with the state of what is recorded on each, written as words (the border style repeats it, never
 * alone). Selecting a tooth shows what is recorded on it by surface and, for people who may change the chart, lets them record a finding, move it forward (Diagnosed, Planned,
 * Completed) or withdraw a wrong entry with a reason. The chart only ever shows what is recorded: an empty chart says so and never implies healthy teeth, and a failed load says so
 * rather than showing an empty chart. Findings on primary teeth are not drawn on this chart (a limit of this release) and are listed below it so none is hidden.
 *
 * Every change is sent as it is made and the chart is replaced by what the server returns; the status line says in words whether the last change was saved, is saving or failed.
 * A stale change shows the shared conflict banner and a reload that refreshes in place, so an open form keeps what was typed. A dropped connection keeps what was typed too.
 */
export function OdontogramPanel({ patient, numbering = DEFAULT_NUMBERING, canWrite = false }: Props) {
  const [chart, setChart] = useState<Chart | null>(null);
  const [failed, setFailed] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const [surface, setSurface] = useState<string | null>(null);
  const [saving, setSaving] = useState<Saving>({ kind: "idle" });
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);

  const adopt = useCallback((next: Chart) => {
    setChart(next);
    setConflict(null);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    getChart(patient.id, controller.signal).then(adopt).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [patient.id, adopt]);

  const byTooth = useMemo(() => findingsByTooth(chart?.findings ?? []), [chart]);
  const drawn = useMemo(() => new Set([...UPPER_PERMANENT, ...LOWER_PERMANENT]), []);
  const notDrawn: Finding[] = (chart?.findings ?? []).filter((f) => !drawn.has(f.toothKey));
  const busy = saving.kind === "saving";

  async function run(change: () => Promise<Chart>, savedText: string): Promise<SaveResult> {
    setSaving({ kind: "saving" });
    try {
      adopt(await change());
      setSaving({ kind: "saved", text: savedText });
      return null;
    } catch (err) {
      if (isConcurrencyConflict(err)) {
        setConflict({ ...err.body, entityType: "tooth finding" });             // the shared banner prints the entity name as given, so give it words, not "ToothFinding"
        setSaving({ kind: "failed", text: "Not saved: someone else changed this." });
      } else if (isNetworkFailure(err)) {
        setSaving({ kind: "failed", text: "Not saved: the connection dropped. What you typed is still here; try again." });
      } else {
        setSaving({ kind: "failed", text: `Not saved: ${err instanceof ApiError ? err.message : "something went wrong."}` });
      }
      return clinicalFieldErrorsOf(err);
    }
  }

  async function reload() {
    try {
      adopt(await getChart(patient.id));
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
        Permanent teeth, numbered with the {numbering === "Fdi" ? "FDI" : numbering} system. The word under each number is the state of what is recorded on that tooth. An empty tooth means
        nothing is recorded, not that it is healthy.
      </p>
      <p className="alv-clinical__status" role="status" aria-live="polite">
        {saving.kind === "saving" ? "Saving…" : saving.kind === "idle" ? (canWrite ? "Every change is saved as you make it." : "You can read this but your role cannot change it.") : saving.text}
      </p>
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => void reload()} />}

      <ul className="alv-odonto__legend" aria-label="Legend">
        {ALL_STATES.map((s) => (
          <li key={s}><span className={`alv-odonto__swatch ${stateClass(s)}`} aria-hidden="true" /> <strong>{s}</strong> <span className="alv-clinical__meta">{STATE_MEANING[s]}</span></li>
        ))}
      </ul>

      {chart.findings.length === 0 && <p className="alv-clinical__meta">No findings are recorded on any tooth.</p>}

      <ToothChart byTooth={byTooth} numbering={numbering} selected={selected} onSelect={(k) => { setSelected(k === selected ? null : k); setSurface(null); }} />

      {selected ? (
        <ToothDetail
          key={selected}
          patientId={patient.id}
          toothKey={selected}
          numbering={numbering}
          findings={byTooth.get(selected) ?? []}
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
          <h3 id="alv-odonto-undrawn" className="alv-clinical__section-title">Recorded on teeth not drawn on this chart</h3>
          <p className="alv-clinical__meta">This chart draws permanent teeth only. {plural(notDrawn.length, "finding is", "findings are")} recorded on primary teeth:</p>
          <ul className="alv-clinical__entries">
            {notDrawn.map((f) => (
              <li key={f.id}>Tooth {displayTooth(f.toothKey, numbering)} ({toothName(f.toothKey)}): {CONDITION_LABELS[f.condition] ?? f.condition}{f.surface ? `, ${SURFACE_NAMES[f.surface]} surface` : ", whole tooth"}: {f.state}</li>
            ))}
          </ul>
        </section>
      )}
    </section>
  );
}
