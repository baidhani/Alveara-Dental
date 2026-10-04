import type { Finding } from "../../services/odontogramApi";
import { headlineState, plural, stateClass } from "./odontogramText";
import { LOWER_PERMANENT, UPPER_PERMANENT, displayTooth, toothName } from "./toothNumbering";
import type { NumberingSystem } from "./toothNumbering";

interface Props {
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

function Arch({ title, keys, ...rest }: Props & { title: string; keys: string[] }) {
  return (
    <div role="group" aria-label={title}>
      <ul className="alv-odonto__arch">
        {keys.map((k) => <Tooth key={k} toothKey={k} findings={rest.byTooth.get(k) ?? []} numbering={rest.numbering} selected={rest.selected === k} onSelect={rest.onSelect} />)}
      </ul>
    </div>
  );
}

/**
 * STORY-006: the 32 permanent teeth as a dentist looks at the patient (the patient's right is on the viewer's left). A tooth is a button; the number on it is whatever the numbering
 * system says, and its state is written as a word under the number (the border style says the same, but never alone). Selecting a tooth opens its details.
 */
export function ToothChart(props: Props) {
  return (
    <div className="alv-odonto__chart">
      <p className="alv-odonto__side"><span>Patient's right</span><span>Patient's left</span></p>
      <Arch {...props} title="Upper teeth" keys={UPPER_PERMANENT} />
      <Arch {...props} title="Lower teeth" keys={LOWER_PERMANENT} />
    </div>
  );
}
