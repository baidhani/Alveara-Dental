import { useState } from "react";
import type { KeyboardEvent } from "react";
import type { PerioReadingInput } from "../../services/perioApi";
import { displayTooth, toothName } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { mmProblem } from "./perioChartRules";
import { CUE_TEXT, cuesOf, describeSite } from "./perioSiteModel";

export interface Measures { suppuration: boolean; plaque: boolean }

interface Props {
  toothKey: string;
  site: string;
  numbering: NumberingSystem;
  /** What is already entered here (from the draft or typed earlier), so going back shows it and retyping corrects it. */
  existing: PerioReadingInput | undefined;
  /** Which optional measures are being recorded at this visit; one that is not recorded stays "not assessed". */
  measures: Measures;
  /** Where this site is in the entry order, counting from 1. */
  position: { number: number; total: number };
  onCommit: (reading: PerioReadingInput) => void;
  onBack: () => void;
  onClear: () => void;
  onSkipTooth: () => void;
}

/**
 * The one site being charted, with its depth, recession and flags. Built to be driven from the keyboard without leaving the depth box: type the depth and press Enter, type the recession (0 if none)
 * and press Enter to save the site and land on the next one; Shift+Enter goes back one site. While typing, B toggles bleeding, S pus, P plaque and X marks the whole tooth as not charted. Mounted anew
 * for every site (the parent keys it), so each site starts from what is already entered there and the depth box takes focus.
 */
export function PerioEntryForm({ toothKey, site, numbering, existing, measures, position, onCommit, onBack, onClear, onSkipTooth }: Props) {
  const [pd, setPd] = useState(existing ? String(existing.probingDepthMm) : "");
  const [rec, setRec] = useState(existing ? String(existing.recessionMm) : "");
  const [bleeding, setBleeding] = useState(existing?.bleeding ?? false);
  const [pus, setPus] = useState(existing?.suppuration === true);
  const [plaque, setPlaque] = useState(existing?.plaque === true);
  const [errors, setErrors] = useState<{ pd?: string; rec?: string }>({});

  const tooth = displayTooth(toothKey, numbering);
  const label = `Tooth ${tooth}, ${describeSite(toothKey, site)}`;

  function commit() {
    const next = { pd: pd.trim() === "" ? "Enter the probing depth for this site." : mmProblem(pd, "Probing depth") ?? undefined, rec: rec.trim() === "" ? "Enter the recession for this site, or 0 if there is none." : mmProblem(rec, "Recession") ?? undefined };
    setErrors(next);
    if (next.pd || next.rec) {
      (document.getElementById(next.pd ? "alv-perio-pd" : "alv-perio-rec") as HTMLInputElement | null)?.focus();
      return;
    }
    onCommit({
      toothKey, site, probingDepthMm: Number(pd), recessionMm: Number(rec), bleeding,
      suppuration: measures.suppuration ? pus : null, plaque: measures.plaque ? plaque : null,
    });
  }

  function onKey(field: "pd" | "rec", e: KeyboardEvent<HTMLInputElement>) {
    const key = e.key.toLowerCase();
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    if (key === "b") { e.preventDefault(); setBleeding((v) => !v); }
    else if (key === "s" && measures.suppuration) { e.preventDefault(); setPus((v) => !v); }
    else if (key === "p" && measures.plaque) { e.preventDefault(); setPlaque((v) => !v); }
    else if (key === "x") { e.preventDefault(); onSkipTooth(); }
    else if (e.key === "Enter") {
      e.preventDefault();
      if (e.shiftKey) onBack();
      else if (field === "pd") {
        const problem = pd.trim() === "" ? "Enter the probing depth for this site." : mmProblem(pd, "Probing depth") ?? undefined;
        setErrors((x) => ({ ...x, pd: problem }));
        if (!problem) document.getElementById("alv-perio-rec")?.focus();
      }
      else commit();
    }
  }

  const cues = pd.trim() !== "" && !mmProblem(pd, "Probing depth") && !mmProblem(rec.trim() === "" ? "0" : rec, "Recession")
    ? cuesOf({ probingDepthMm: Number(pd), recessionMm: Number(rec || 0), bleeding, suppuration: measures.suppuration ? pus : null }) : [];

  return (
    <form className="alv-perio__entry" aria-label="Chart this site" onSubmit={(e) => { e.preventDefault(); commit(); }}>
      <h3 className="alv-perio__where" aria-live="polite">
        {label} <span className="alv-clinical__meta">(FDI {toothKey}, {toothName(toothKey)}) · site {position.number} of {position.total}</span>
      </h3>
      <div className="alv-perio__fields">
        <label>Probing depth (mm)
          <input id="alv-perio-pd" type="text" inputMode="numeric" autoComplete="off" maxLength={2} autoFocus value={pd} aria-invalid={errors.pd ? true : undefined} aria-describedby={errors.pd ? "alv-perio-pd-err" : undefined}
            onChange={(e) => setPd(e.target.value.replace(/\D/g, ""))} onKeyDown={(e) => onKey("pd", e)} />
        </label>
        <label>Recession (mm)
          <input id="alv-perio-rec" type="text" inputMode="numeric" autoComplete="off" maxLength={2} value={rec} aria-invalid={errors.rec ? true : undefined} aria-describedby={errors.rec ? "alv-perio-rec-err" : undefined}
            onChange={(e) => setRec(e.target.value.replace(/\D/g, ""))} onKeyDown={(e) => onKey("rec", e)} />
        </label>
        <label><input type="checkbox" checked={bleeding} onChange={(e) => setBleeding(e.target.checked)} /> Bleeding on probing (B)</label>
        {measures.suppuration && <label><input type="checkbox" checked={pus} onChange={(e) => setPus(e.target.checked)} /> Pus (S)</label>}
        {measures.plaque && <label><input type="checkbox" checked={plaque} onChange={(e) => setPlaque(e.target.checked)} /> Plaque (P)</label>}
      </div>
      {errors.pd && <p id="alv-perio-pd-err" className="alv-perio__fielderror">{errors.pd}</p>}
      {errors.rec && <p id="alv-perio-rec-err" className="alv-perio__fielderror">{errors.rec}</p>}
      {cues.length > 0 && (
        <p className="alv-clinical__meta" aria-label="Cues for this reading">
          Worth a second look: {cues.map((c) => CUE_TEXT[c]).join(", ")}. These marks only draw the eye; they are not a diagnosis.
        </p>
      )}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary">Save site and go on</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onBack}>Previous site</button>
        {existing && <button type="button" className="btn btn-outline-secondary" onClick={onClear}>Clear this site</button>}
        <button type="button" className="btn btn-outline-secondary" onClick={onSkipTooth}>Tooth not charted (X)</button>
      </div>
    </form>
  );
}
