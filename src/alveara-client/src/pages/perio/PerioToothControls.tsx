import type { PerioToothInput } from "../../services/perioApi";
import { displayTooth } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { GRADES, isMultiRooted } from "./perioSiteModel";

interface Props {
  toothKey: string;
  numbering: NumberingSystem;
  record: PerioToothInput | undefined;
  onChange: (record: PerioToothInput) => void;
}

const toGrade = (v: string): number | null => (v === "" ? null : Number(v));

/** What is recorded about the whole tooth: mobility (Miller grade 0 to 3) and, on a multi-rooted tooth only, furcation (grade 0 to 3). Blank means not assessed. */
export function PerioToothControls({ toothKey, numbering, record, onChange }: Props) {
  const tooth = displayTooth(toothKey, numbering);
  const base = { toothKey, mobility: record?.mobility ?? null, furcation: record?.furcation ?? null, excluded: false };
  return (
    <fieldset className="alv-perio__tooth">
      <legend>Tooth {tooth} as a whole</legend>
      <label>Mobility (grade)
        <select value={record?.mobility ?? ""} onChange={(e) => onChange({ ...base, mobility: toGrade(e.target.value) })}>
          <option value="">Not assessed</option>
          {GRADES.map((g) => <option key={g} value={g}>{g}</option>)}
        </select>
      </label>
      {isMultiRooted(toothKey)
        ? (
          <label>Furcation (grade)
            <select value={record?.furcation ?? ""} onChange={(e) => onChange({ ...base, furcation: toGrade(e.target.value) })}>
              <option value="">Not assessed</option>
              {GRADES.map((g) => <option key={g} value={g}>{g}</option>)}
            </select>
          </label>
        )
        : <p className="alv-clinical__meta">This tooth has a single root, so there is no furcation to grade.</p>}
    </fieldset>
  );
}
