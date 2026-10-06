import { ALL_KEYS, LOWER_PERMANENT, UPPER_PERMANENT, displayTooth, toothName } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { CODING_SYSTEMS, CODE_MAX, REGIONS, REGION_LABELS, SOURCES, SOURCE_NOTE_MAX } from "./diagnosisStructureRules";
import type { StructureDraft } from "./diagnosisStructureRules";

const TEETH = [...UPPER_PERMANENT, ...LOWER_PERMANENT, ...ALL_KEYS.filter((k) => !UPPER_PERMANENT.includes(k) && !LOWER_PERMANENT.includes(k))];

/** What the parent form gives its fields: one id per field, and how to point at a field that has a problem. */
export interface FieldHelpers {
  ids: Record<string, string>;
  invalid: (field: string) => boolean | undefined;
  describe: (field: string) => string | undefined;
}

interface ToothProps extends FieldHelpers { value: string; numbering: NumberingSystem; onChange: (key: string) => void }

/** The tooth a diagnosis is about, or none. A diagnosis is about one tooth or one region: choosing a tooth clears the region (the parent does that in onChange). */
export function ToothSelect({ value, numbering, onChange, ids, invalid, describe }: ToothProps) {
  return (
    <label htmlFor={ids.toothKey}>Tooth (optional)
      <select id={ids.toothKey} value={value} onChange={(e) => onChange(e.target.value)} aria-invalid={invalid("toothKey")} aria-describedby={describe("toothKey")}>
        <option value="">Not about one tooth</option>
        {TEETH.map((k) => <option key={k} value={k}>{displayTooth(k, numbering)} - {toothName(k)}</option>)}
      </select>
    </label>
  );
}

interface Props extends FieldHelpers {
  value: StructureDraft;
  onChange: (patch: Partial<StructureDraft>) => void;
  /** Whether to offer the oral region (the amendment form and the record form do; both clear the tooth when a region is chosen). */
  showRegion?: boolean;
}

/**
 * ALV-013-C01: the structured parts of a diagnosis: the oral region (instead of a tooth), the optional coding (system and code together; only the system's name is checked, never the code against a code
 * set) and where the diagnosis came from. Every box is optional: a diagnosis is stored structurally with no coding at all.
 */
export function StructureFields({ value, onChange, ids, invalid, describe, showRegion = true }: Props) {
  return (
    <>
      {showRegion && (
        <>
          <label htmlFor={ids.regionKey}>Oral region (optional)
            <select id={ids.regionKey} value={value.regionKey} onChange={(e) => onChange({ regionKey: e.target.value })} aria-invalid={invalid("regionKey")} aria-describedby={describe("regionKey")}>
              <option value="">Not about a region</option>
              {REGIONS.map((r) => <option key={r} value={r}>{REGION_LABELS[r]}</option>)}
            </select>
          </label>
          <span className="alv-clinical__meta">A diagnosis is about one tooth or one region, not both.</span>
        </>
      )}
      <fieldset className="alv-dx__plan">
        <legend>Coding (optional)</legend>
        <p className="alv-clinical__meta">Leave this empty if the diagnosis has no code. When it has one, give the system and the code together. The code is kept as typed and is not checked against any code list.</p>
        <label htmlFor={ids.codingSystem}>Coding system
          <select id={ids.codingSystem} value={value.codingSystem} onChange={(e) => onChange({ codingSystem: e.target.value })} aria-invalid={invalid("codingSystem")} aria-describedby={describe("codingSystem")}>
            <option value="">No coding system</option>
            {CODING_SYSTEMS.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </label>
        <label htmlFor={ids.code}>Code
          <input id={ids.code} type="text" maxLength={CODE_MAX + 20} value={value.code} onChange={(e) => onChange({ code: e.target.value })} aria-invalid={invalid("code")} aria-describedby={describe("code")} />
        </label>
      </fieldset>
      <fieldset className="alv-dx__plan">
        <legend>Source</legend>
        <label htmlFor={ids.source}>Where it came from
          <select id={ids.source} value={value.source} onChange={(e) => onChange({ source: e.target.value })} aria-invalid={invalid("source")} aria-describedby={describe("source")}>
            <option value="">Entered here</option>
            {SOURCES.filter((s) => s !== "Manual").map((s) => <option key={s} value={s}>{s === "Imported" ? "Imported from another record" : "Mapped from an earlier entry"}</option>)}
          </select>
        </label>
        {value.source !== "" && (
          <label htmlFor={ids.sourceNote}>Source note (optional)
            <input id={ids.sourceNote} type="text" maxLength={SOURCE_NOTE_MAX + 50} value={value.sourceNote} onChange={(e) => onChange({ sourceNote: e.target.value })} aria-invalid={invalid("sourceNote")} aria-describedby={describe("sourceNote")} />
          </label>
        )}
      </fieldset>
    </>
  );
}
