import { useEffect, useMemo, useState } from "react";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { CONDITION_LABELS, getChart } from "../../services/odontogramApi";
import type { Chart, Finding } from "../../services/odontogramApi";
import type { PatientDetail } from "../../services/patientsApi";
import { ToothChart } from "./ToothChart";
import { ToothDetail } from "./ToothDetail";
import { ALL_STATES, STATE_MEANING, findingsByTooth, plural, stateClass } from "./odontogramText";
import { DEFAULT_NUMBERING, UPPER_PERMANENT, LOWER_PERMANENT, SURFACE_NAMES, displayTooth, toothName } from "./toothNumbering";
import type { NumberingSystem } from "./toothNumbering";
import "./Odontogram.css";

/**
 * STORY-006: the patient's odontogram. The chart shows the 32 permanent teeth with the state of what is recorded on each, written as words (the border style repeats it, never
 * alone). Selecting a tooth shows what is recorded on it by surface. The chart only ever shows what is recorded: an empty chart says so and never implies healthy teeth, and a failed
 * load says so rather than showing an empty chart. Findings on primary teeth are not drawn on this chart (a limit of this release) and are listed below it so none is hidden.
 *
 * The numbering system is a parameter and defaults to Universal; a practice-level preference for it is not built yet. The chart is read-only: the patient header above carries the
 * patient-safety strip on this screen, as on every patient screen.
 */
export function OdontogramPanel({ patient, numbering = DEFAULT_NUMBERING }: { patient: PatientDetail; numbering?: NumberingSystem }) {
  const [chart, setChart] = useState<Chart | null>(null);
  const [failed, setFailed] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const [surface, setSurface] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    getChart(patient.id, controller.signal).then(setChart).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [patient.id]);

  const byTooth = useMemo(() => findingsByTooth(chart?.findings ?? []), [chart]);
  const drawn = useMemo(() => new Set([...UPPER_PERMANENT, ...LOWER_PERMANENT]), []);
  const notDrawn: Finding[] = (chart?.findings ?? []).filter((f) => !drawn.has(f.toothKey));

  if (failed) return <ErrorState title="Could not load the odontogram" description="Do not assume nothing is recorded. Check your connection and reload the page." />;
  if (chart === null) return <LoadingState label="Loading the odontogram…" />;

  return (
    <section className="alv-clinical alv-odonto" aria-labelledby="alv-odonto-title">
      <h2 id="alv-odonto-title" className="alv-workspace__section-title">Odontogram</h2>
      <p className="alv-clinical__meta">
        Permanent teeth, numbered with the {numbering === "Fdi" ? "FDI" : numbering} system. The word under each number is the state of what is recorded on that tooth. An empty tooth means
        nothing is recorded, not that it is healthy.
      </p>

      <ul className="alv-odonto__legend" aria-label="Legend">
        {ALL_STATES.map((s) => (
          <li key={s}><span className={`alv-odonto__swatch ${stateClass(s)}`} aria-hidden="true" /> <strong>{s}</strong> <span className="alv-clinical__meta">{STATE_MEANING[s]}</span></li>
        ))}
      </ul>

      {chart.findings.length === 0 && <p className="alv-clinical__meta">No findings are recorded on any tooth.</p>}

      <ToothChart byTooth={byTooth} numbering={numbering} selected={selected} onSelect={(k) => { setSelected(k === selected ? null : k); setSurface(null); }} />

      {selected ? (
        <ToothDetail toothKey={selected} numbering={numbering} findings={byTooth.get(selected) ?? []} selectedSurface={surface} onSurface={setSurface} />
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
