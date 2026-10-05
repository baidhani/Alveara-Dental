import type { KeyboardEvent } from "react";
import type { Finding } from "../../services/odontogramApi";
import { headlineState, plural, stateClass } from "./odontogramText";
import { archesFor } from "./dentition";
import type { DentitionView } from "./dentition";
import { displayTooth, toothName } from "./toothNumbering";
import type { NumberingSystem } from "./toothNumbering";

interface Props {
  view: DentitionView;
  byTooth: Map<string, Finding[]>;
  numbering: NumberingSystem;
  selected: string | null;
  onSelect: (toothKey: string) => void;
}

function Tooth({ toothKey, findings, numbering, selected, onSelect }: { toothKey: string; findings: Finding[]; numbering: NumberingSystem; selected: boolean; onSelect: (k: string) => void }) {
  const state = headlineState(findings);
  const label = displayTooth(toothKey, numbering);
  const summary = findings.length === 0 ? "nothing recorded" : `${plural(findings.length, "finding", "findings")}, ${state}`;
  return (
    <li>
      <button
        type="button"
        data-tooth={toothKey}
        className={`alv-odonto__tooth ${state ? stateClass(state) : "alv-odonto__state--none"}${selected ? " alv-odonto__tooth--selected" : ""}`}
        aria-pressed={selected}
        aria-label={`Tooth ${label}, ${toothName(toothKey)}: ${summary}`}
        onClick={() => onSelect(toothKey)}
      >
        <span className="alv-odonto__tooth-number">{label}</span>
        <span className="alv-odonto__tooth-state" aria-hidden="true">{findings.length === 0 ? "—" : `${state}${findings.length > 1 ? ` +${findings.length - 1}` : ""}`}</span>
      </button>
    </li>
  );
}

/**
 * Arrow keys move between teeth without leaving the chart: left and right within an arch, home and end to its ends, up and down to the nearest tooth in the arch above or below. Every tooth
 * stays a normal Tab stop and Enter or Space still selects, so this is a shortcut, never the only way.
 */
function moveFocus(event: KeyboardEvent<HTMLDivElement>) {
  const key = event.key;
  if (!["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"].includes(key)) return;
  const current = (event.target as HTMLElement).closest<HTMLButtonElement>("button.alv-odonto__tooth");
  if (!current) return;
  const arch = current.closest("ul")!;
  const inArch = [...arch.querySelectorAll<HTMLButtonElement>("button.alv-odonto__tooth")];
  const at = inArch.indexOf(current);
  let next: HTMLButtonElement | undefined;
  if (key === "ArrowLeft") next = inArch[at - 1];
  else if (key === "ArrowRight") next = inArch[at + 1];
  else if (key === "Home") next = inArch[0];
  else if (key === "End") next = inArch[inArch.length - 1];
  else {
    const arches = [...event.currentTarget.querySelectorAll<HTMLUListElement>("ul.alv-odonto__arch")];
    const other = arches[arches.indexOf(arch) + (key === "ArrowDown" ? 1 : -1)];
    const x = current.getBoundingClientRect().left + current.getBoundingClientRect().width / 2;
    next = other && [...other.querySelectorAll<HTMLButtonElement>("button.alv-odonto__tooth")].sort((a, b) => Math.abs(centre(a) - x) - Math.abs(centre(b) - x))[0];
  }
  if (!next) return;
  event.preventDefault();
  next.focus();
}
const centre = (el: HTMLElement) => el.getBoundingClientRect().left + el.getBoundingClientRect().width / 2;

/**
 * STORY-006 / ALV-006-C01: the teeth as a dentist looks at the patient (the patient's right is on the viewer's left): the permanent teeth, the primary teeth, or both. A tooth is a button; the
 * number on it is whatever the numbering system says, and its state is written as a word under the number (the border style says the same, but never alone). Selecting a tooth opens its
 * details.
 */
export function ToothChart({ view, byTooth, numbering, selected, onSelect }: Props) {
  return (
    <div className="alv-odonto__chart" onKeyDown={moveFocus}>
      <p className="alv-odonto__side"><span>Patient's right</span><span>Patient's left</span></p>
      {archesFor(view).map((arch) => (
        <div key={arch.title} role="group" aria-label={arch.title}>
          <ul className={`alv-odonto__arch${arch.primary ? " alv-odonto__arch--primary" : ""}`}>
            {arch.keys.map((k) => <Tooth key={k} toothKey={k} findings={byTooth.get(k) ?? []} numbering={numbering} selected={selected === k} onSelect={onSelect} />)}
          </ul>
        </div>
      ))}
    </div>
  );
}
