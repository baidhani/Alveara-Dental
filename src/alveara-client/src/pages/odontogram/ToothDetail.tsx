import { useState } from "react";
import { Button } from "../../components/Button";
import { recordFinding } from "../../services/odontogramApi";
import type { Finding } from "../../services/odontogramApi";
import { FindingRow } from "./FindingRow";
import type { RunChange } from "./FindingRow";
import { RecordFindingForm } from "./RecordFindingForm";
import { STATE_MEANING, describeFinding, plural } from "./odontogramText";
import { SURFACE_NAMES, displayTooth, surfacesFor, toothName } from "./toothNumbering";
import type { NumberingSystem } from "./toothNumbering";

interface Props {
  patientId: string;
  toothKey: string;
  numbering: NumberingSystem;
  findings: Finding[];
  selectedSurface: string | null;
  onSurface: (surface: string | null) => void;
  canWrite: boolean;
  busy: boolean;
  run: RunChange;
}

/**
 * STORY-006: one tooth - what is recorded on it, by surface, and (for people who may change the chart) the controls to record a finding, move it forward or withdraw it. The tooth is
 * named in every numbering system (the one on the chart first) so a chart read in another system can be matched. Selecting a surface narrows the list to that surface plus the
 * whole-tooth findings and starts the record form on that surface; selecting it again shows everything. Changes are made here, never on the chart itself.
 */
export function ToothDetail({ patientId, toothKey, numbering, findings, selectedSurface, onSurface, canWrite, busy, run }: Props) {
  const [adding, setAdding] = useState(false);
  const surfaces = surfacesFor(toothKey);
  const shown = selectedSurface ? findings.filter((f) => f.surface === selectedSurface || f.surface === null) : findings;
  const others = (["Universal", "Fdi", "Palmer"] as NumberingSystem[]).filter((s) => s !== numbering);
  const label = displayTooth(toothKey, numbering);
  return (
    <section className="alv-odonto__detail" aria-labelledby="alv-odonto-tooth-title">
      <h3 id="alv-odonto-tooth-title" className="alv-clinical__section-title">Tooth {label}: {toothName(toothKey)}</h3>
      <p className="alv-clinical__meta">
        Also written {others.map((s) => `${s === "Fdi" ? "FDI" : s} ${displayTooth(toothKey, s)}`).join(" · ")}.
      </p>

      <div role="group" aria-label="Surfaces" className="alv-odonto__surfaces">
        {surfaces.map((code) => {
          const count = findings.filter((f) => f.surface === code).length;
          const selected = selectedSurface === code;
          return (
            <button
              key={code}
              type="button"
              className={`alv-odonto__surface${selected ? " alv-odonto__surface--selected" : ""}${count > 0 ? " alv-odonto__surface--has" : ""}`}
              aria-pressed={selected}
              onClick={() => onSurface(selected ? null : code)}
            >
              <span>{SURFACE_NAMES[code]}</span>
              <span className="alv-odonto__surface-count">{count === 0 ? "none recorded" : plural(count, "finding", "findings")}</span>
            </button>
          );
        })}
      </div>

      {findings.length === 0 ? (
        <p className="alv-clinical__meta">Nothing is recorded for this tooth. This does not mean it is healthy.</p>
      ) : shown.length === 0 ? (
        <p className="alv-clinical__meta">Nothing is recorded for the {SURFACE_NAMES[selectedSurface!].toLowerCase()} surface. Select it again to see everything on this tooth.</p>
      ) : (
        <ul className="alv-clinical__entries" aria-label={selectedSurface ? `Findings on the ${SURFACE_NAMES[selectedSurface].toLowerCase()} surface and the whole tooth` : "Findings on this tooth"}>
          {shown.map((f) => <FindingRow key={f.id} finding={f} toothLabel={label} canWrite={canWrite} busy={busy} run={run} />)}
        </ul>
      )}

      {canWrite && (
        adding ? (
          <RecordFindingForm
            toothKey={toothKey}
            defaultSurface={selectedSurface}
            busy={busy}
            onCancel={() => setAdding(false)}
            onSubmit={async (input) => {
              const refused = await run(() => recordFinding(patientId, input), `${describeFinding(input.condition, input.surface ? SURFACE_NAMES[input.surface] : null)} recorded on tooth ${label} as ${input.state}.`);
              if (!refused) setAdding(false);
              return refused;
            }}
          />
        ) : (
          <div className="alv-clinical__section-actions"><Button type="button" onClick={() => setAdding(true)} disabled={busy}>Record a finding on this tooth</Button></div>
        )
      )}
      <p className="alv-clinical__meta">{Object.entries(STATE_MEANING).map(([s, m]) => `${s}: ${m}`).join(" · ")}.</p>
    </section>
  );
}
