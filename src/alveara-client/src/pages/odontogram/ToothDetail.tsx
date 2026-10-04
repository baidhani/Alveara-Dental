import { CONDITION_LABELS } from "../../services/odontogramApi";
import type { Finding } from "../../services/odontogramApi";
import { STATE_MEANING, plural, stateClass, when } from "./odontogramText";
import { SURFACE_NAMES, displayTooth, surfacesFor, toothName } from "./toothNumbering";
import type { NumberingSystem } from "./toothNumbering";

interface Props {
  toothKey: string;
  numbering: NumberingSystem;
  findings: Finding[];
  selectedSurface: string | null;
  onSurface: (surface: string | null) => void;
}

function FindingRow({ finding }: { finding: Finding }) {
  return (
    <li className="alv-odonto__finding">
      <p className="alv-odonto__finding-title">
        <strong>{CONDITION_LABELS[finding.condition] ?? finding.condition}</strong>
        <span> · {finding.surface ? `${SURFACE_NAMES[finding.surface]} surface` : "Whole tooth"}</span>
        <span className={`alv-odonto__chip ${stateClass(finding.state)}`}>{finding.state}</span>
      </p>
      <p className="alv-odonto__finding-meta">
        Recorded by {finding.recordedByName ?? "Staff member"} on {when(finding.recordedAtUtc)}
        {finding.updatedAtUtc ? `; last changed by ${finding.updatedByName ?? "Staff member"} on ${when(finding.updatedAtUtc)}` : ""}.
      </p>
    </li>
  );
}

/**
 * STORY-006: one tooth - what is recorded on it, by surface. The tooth is named in every numbering system (the one on the chart first) so a chart read in another system can be matched.
 * Selecting a surface narrows the list to that surface plus the whole-tooth findings; selecting it again shows everything. Read-only here: changes are made from this panel in the
 * next step, never from the chart itself.
 */
export function ToothDetail({ toothKey, numbering, findings, selectedSurface, onSurface }: Props) {
  const surfaces = surfacesFor(toothKey);
  const shown = selectedSurface ? findings.filter((f) => f.surface === selectedSurface || f.surface === null) : findings;
  const others = (["Universal", "Fdi", "Palmer"] as NumberingSystem[]).filter((s) => s !== numbering);
  return (
    <section className="alv-odonto__detail" aria-labelledby="alv-odonto-tooth-title">
      <h3 id="alv-odonto-tooth-title" className="alv-clinical__section-title">Tooth {displayTooth(toothKey, numbering)}: {toothName(toothKey)}</h3>
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
          {shown.map((f) => <FindingRow key={f.id} finding={f} />)}
        </ul>
      )}
      <p className="alv-clinical__meta">{Object.entries(STATE_MEANING).map(([s, m]) => `${s}: ${m}`).join(" · ")}.</p>
    </section>
  );
}
