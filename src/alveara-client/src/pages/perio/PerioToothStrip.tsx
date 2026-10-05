import type { PerioToothInput } from "../../services/perioApi";
import { ARCH_ORDER } from "./perioSiteModel";
import { toothState } from "./perioEntryState";
import { displayTooth, toothName } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";

interface Props {
  numbering: NumberingSystem;
  readings: ReadonlyMap<string, unknown>;
  teeth: ReadonlyMap<string, PerioToothInput>;
  absent: readonly string[];
  current: string | null;
  onJump: (toothKey: string) => void;
  onInclude: (toothKey: string) => void;
}

/** The 32 teeth in arch order, each with its state in words; a tooth can be jumped to, and one marked not charted can be put back. */
export function PerioToothStrip({ numbering, readings, teeth, absent, current, onJump, onInclude }: Props) {
  return (
    <ul className="alv-perio__strip" aria-label="Teeth in this chart">
      {ARCH_ORDER.map((key) => {
        const state = toothState(key, readings, teeth, absent);
        const skipped = state === "not charted" || state.startsWith("missing");
        return (
          <li key={key} className={`alv-perio__chip${current === key ? " alv-perio__chip--current" : ""}${skipped ? " alv-perio__chip--skipped" : ""}`}>
            {skipped && state === "not charted" ? (
              <button type="button" onClick={() => onInclude(key)} aria-label={`Tooth ${displayTooth(key, numbering)}, ${toothName(key)}: not charted. Chart this tooth`}>
                <strong>{displayTooth(key, numbering)}</strong><span>{state}</span>
              </button>
            ) : (
              <button type="button" disabled={skipped} aria-current={current === key ? "true" : undefined} onClick={() => onJump(key)} aria-label={`Tooth ${displayTooth(key, numbering)}, ${toothName(key)}: ${state}`}>
                <strong>{displayTooth(key, numbering)}</strong><span>{state}</span>
              </button>
            )}
          </li>
        );
      })}
    </ul>
  );
}
