/**
 * STORY-012: what the chart grid knows about periodontal data, so a mistake is caught before anything is sent. These mirror the server's rules (PerioRules.cs) and are only a convenience:
 * the server judges every save again and its messages are the ones shown if it disagrees.
 */
import type { PerioReadingInput } from "../../services/perioApi";

export const MIN_MM = 0;
export const MAX_MM = 15;

/** The six sites of a tooth, in the order shown: distal, middle and mesial on the cheek side, then the same on the tongue side. */
export const SITES = ["DB", "B", "MB", "DL", "L", "ML"] as const;
export type Site = (typeof SITES)[number];
export const SITE_NAMES: Record<Site, string> = {
  DB: "distal buccal", B: "mid buccal", MB: "mesial buccal", DL: "distal lingual", L: "mid lingual", ML: "mesial lingual",
};

/** What the person typed for one site. Bleeding is a checkbox, so unchecked is an explicit "no". */
export interface CellDraft {
  pd: string;
  rec: string;
  bleeding: boolean;
}
export const EMPTY_CELL: CellDraft = { pd: "", rec: "", bleeding: false };
export type Draft = Record<string, CellDraft>;
export const cellId = (toothKey: string, site: string) => `${toothKey}:${site}`;

/** A problem with one cell: which of its fields, and what to do about it. */
export interface CellProblem {
  toothKey: string;
  site: string;
  field: "pd" | "rec";
  message: string;
}

/** Null when `text` is a whole number from 0 to 15 mm. */
export function mmProblem(text: string, label: string): string | null {
  if (!/^\d+$/.test(text.trim()) || Number(text) < MIN_MM || Number(text) > MAX_MM)
    return `${label} must be a whole number from ${MIN_MM} to ${MAX_MM} mm.`;
  return null;
}

/** A site counts as entered once anything is typed in it; an untouched site is simply not part of the chart. */
export const isTouched = (c: CellDraft | undefined) => !!c && (c.pd.trim() !== "" || c.rec.trim() !== "" || c.bleeding);

/**
 * Judges the whole draft: every touched site needs a probing depth and a recession (0 if there is none), both whole millimetres from 0 to 15. Returns every problem, and the readings
 * to send only when there are none, so nothing partial is ever sent.
 */
export function readingsFromDraft(draft: Draft, toothKeys: readonly string[]): { readings: PerioReadingInput[]; problems: CellProblem[] } {
  const readings: PerioReadingInput[] = [];
  const problems: CellProblem[] = [];
  for (const toothKey of toothKeys) {
    for (const site of SITES) {
      const cell = draft[cellId(toothKey, site)];
      if (!isTouched(cell)) continue;
      const here = (field: "pd" | "rec", message: string) => problems.push({ toothKey, site, field, message });
      if (cell.pd.trim() === "") here("pd", "Enter the probing depth for this site.");
      else {
        const p = mmProblem(cell.pd, "Probing depth");
        if (p) here("pd", p);
      }
      if (cell.rec.trim() === "") here("rec", "Enter the recession for this site, or 0 if there is none.");
      else {
        const p = mmProblem(cell.rec, "Recession");
        if (p) here("rec", p);
      }
    }
  }
  if (problems.length > 0) return { readings: [], problems };
  for (const toothKey of toothKeys)
    for (const site of SITES) {
      const cell = draft[cellId(toothKey, site)];
      if (isTouched(cell)) readings.push({ toothKey, site, probingDepthMm: Number(cell.pd), recessionMm: Number(cell.rec), bleeding: cell.bleeding });
    }
  return { readings, problems };
}

/** The same readings as a string, so a retry of an unchanged chart keeps its key and any change gets a new one. */
export const signatureOf = (readings: PerioReadingInput[]) => JSON.stringify(readings);
