import type { KeyboardEvent } from "react";
import { LOWER_PERMANENT, UPPER_PERMANENT, displayTooth, toothName } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { EMPTY_CELL, SITES, SITE_NAMES, cellId } from "./perioChartRules";
import type { CellDraft, CellProblem, Draft } from "./perioChartRules";

interface Props {
  draft: Draft;
  /** Problems by cell id, so the field that needs correcting is marked and its message is read out with it. */
  problems: Map<string, CellProblem[]>;
  numbering: NumberingSystem;
  disabled: boolean;
  onChange: (id: string, patch: Partial<CellDraft>) => void;
}

const ARCHES = [
  { title: "Upper teeth", keys: UPPER_PERMANENT },
  { title: "Lower teeth", keys: LOWER_PERMANENT },
];

/** Enter moves to the next entry box in reading order (a clinician charting by keyboard never has to reach for the mouse); Shift+Enter goes back. */
function moveOnEnter(e: KeyboardEvent<HTMLInputElement>) {
  if (e.key !== "Enter") return;
  e.preventDefault();
  const all = Array.from(e.currentTarget.closest("section")?.querySelectorAll<HTMLInputElement>("input[data-perio-entry]") ?? []);
  const next = all[all.indexOf(e.currentTarget) + (e.shiftKey ? -1 : 1)];
  next?.focus();
}

/**
 * The entry grid: one row per permanent tooth, six sites each (distal, middle and mesial on the cheek side, then the tongue side), and for every site a probing depth, a recession (whole
 * millimetres, 0 to 15) and a bleeding checkbox (unchecked means no bleeding). A site left completely empty is not part of the chart. Teeth are shown with the numbering system in use;
 * the stored identity is always the FDI key.
 */
export function PerioGrid({ draft, problems, numbering, disabled, onChange }: Props) {
  return (
    <>
      {ARCHES.map((arch) => (
        <div className="alv-perio__arch" key={arch.title}>
          <table className="alv-perio__table">
            <caption>{arch.title}</caption>
            <thead>
              <tr>
                <th scope="col" rowSpan={2}>Tooth</th>
                <th scope="colgroup" colSpan={3}>Cheek side (buccal)</th>
                <th scope="colgroup" colSpan={3}>Tongue side (lingual)</th>
              </tr>
              <tr>
                {SITES.map((s) => <th scope="col" key={s}><abbr title={SITE_NAMES[s]}>{s}</abbr></th>)}
              </tr>
            </thead>
            <tbody>
              {arch.keys.map((key) => (
                <tr key={key}>
                  <th scope="row" title={toothName(key)}>{displayTooth(key, numbering)}</th>
                  {SITES.map((site) => {
                    const id = cellId(key, site);
                    const cell = draft[id] ?? EMPTY_CELL;
                    const issues = problems.get(id) ?? [];
                    const label = (what: string) => `${what}, tooth ${displayTooth(key, numbering)}, ${SITE_NAMES[site]}`;
                    const entry = (field: "pd" | "rec", what: string) => {
                      const issue = issues.find((p) => p.field === field);
                      return (
                        <>
                          <input
                            type="text" inputMode="numeric" autoComplete="off" maxLength={2} className={`alv-perio__mm${issue ? " alv-perio__mm--invalid" : ""}`}
                            aria-label={label(what)} aria-invalid={issue ? true : undefined} aria-describedby={issue ? `${id}-${field}-problem` : undefined}
                            data-perio-entry data-cell={id} data-field={field} value={cell[field]} disabled={disabled} onKeyDown={moveOnEnter}
                            onChange={(e) => onChange(id, { [field]: e.target.value })}
                          />
                          {issue && <span id={`${id}-${field}-problem`} className="alv-perio__sr">{issue.message}</span>}
                        </>
                      );
                    };
                    return (
                      <td key={site} className={`alv-perio__site${cell.bleeding ? " alv-perio__site--bleeding" : ""}`}>
                        {entry("pd", "Probing depth in millimetres")}
                        {entry("rec", "Recession in millimetres")}
                        <input type="checkbox" aria-label={label("Bleeding on probing")} checked={cell.bleeding} disabled={disabled} onChange={(e) => onChange(id, { bleeding: e.target.checked })} />
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ))}
    </>
  );
}
