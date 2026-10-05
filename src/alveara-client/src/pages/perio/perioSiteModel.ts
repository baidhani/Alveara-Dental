/**
 * ALV-012-C01: the six-site model on the client, a mirror of the backend's PerioSiteModel and PerioCues, held to the same results by tests (the same reference sites and a checksum of the whole sweep
 * that the backend tests also assert). The codes (DB, B, MB, DL, L, ML) are the stored identity; what a site is CALLED depends on the tooth and is presentation only. `sweep` is the one documented
 * full-mouth entry order: per arch a pass along the cheek side and back along the tongue side, upper arch then lower, teeth in `excluded` skipped.
 */
import { LOWER_PERMANENT, UPPER_PERMANENT, isAnterior, isPrimary } from "../odontogram/toothNumbering";
import { SITES } from "./perioChartRules";
import type { Site } from "./perioChartRules";

/** The 32 teeth in arch order, upper arch first: the rows of the chart. */
export const ARCH_ORDER: readonly string[] = [...UPPER_PERMANENT, ...LOWER_PERMANENT];

export const isCheekSide = (site: string) => site === "DB" || site === "B" || site === "MB";
export const isUpper = (tooth: string) => tooth[0] === "1" || tooth[0] === "2";
const isRight = (tooth: string) => tooth[0] === "1" || tooth[0] === "4";

/** How a site reads on this tooth, for example "mesial palatal" (upper tooth, tongue side) or "distal facial" (front tooth, cheek side). */
export function describeSite(tooth: string, site: string): string {
  if (!ARCH_ORDER.includes(tooth) || !(SITES as readonly string[]).includes(site)) throw new Error(`Not a site of a permanent tooth: ${tooth} ${site}.`);
  const side = isCheekSide(site) ? (isAnterior(tooth) ? "facial" : "buccal") : isUpper(tooth) ? "palatal" : "lingual";
  const position = site === "B" || site === "L" ? "mid" : site[0] === "D" ? "distal" : "mesial";
  return `${position} ${side}`;
}

export interface SiteRef { toothKey: string; site: string }

const UPPER = UPPER_PERMANENT as readonly string[];
const LOWER = LOWER_PERMANENT as readonly string[];

/** The full-mouth entry order (see the file comment); the same sequence as the backend's `PerioSiteModel.Sweep`. */
export function sweep(excluded: ReadonlySet<string> = new Set()): SiteRef[] {
  const order: SiteRef[] = [];
  for (const arch of [UPPER, LOWER]) {
    for (const toothKey of arch.filter((t) => !excluded.has(t)))
      for (const site of isRight(toothKey) ? ["DB", "B", "MB"] : ["MB", "B", "DB"]) order.push({ toothKey, site });
    for (const toothKey of [...arch].reverse().filter((t) => !excluded.has(t)))
      for (const site of isRight(toothKey) ? ["ML", "L", "DL"] : ["DL", "L", "ML"]) order.push({ toothKey, site });
  }
  return order;
}

/** Molars and the first upper premolars (14, 24) have more than one root, so only they can have a furcation to grade. */
export const isMultiRooted = (tooth: string) => ARCH_ORDER.includes(tooth) && !isPrimary(tooth) && (Number(tooth[1]) >= 6 || tooth === "14" || tooth === "24");

export const MAX_GRADE = 3;
export const GRADES = [0, 1, 2, 3] as const;

// ---------- visual cues: draw the eye, never a diagnosis ----------

/** A probing depth of this many millimetres or more is marked as a deeper pocket. */
export const DEEP_MM = 4;
/** ... and a very deep one at this many. */
export const VERY_DEEP_MM = 6;
/** A change in probing depth of at least this many millimetres, either way, is counted as better or worse. */
export const SIGNIFICANT_CHANGE_MM = 2;

export interface CueInput { probingDepthMm: number; recessionMm: number; bleeding: boolean; suppuration?: boolean | null }
export type Cue = "very_deep" | "deep" | "recession" | "bleeding" | "suppuration";

export function cuesOf(r: CueInput): Cue[] {
  const cues: Cue[] = [];
  if (r.probingDepthMm >= VERY_DEEP_MM) cues.push("very_deep");
  else if (r.probingDepthMm >= DEEP_MM) cues.push("deep");
  if (r.recessionMm > 0) cues.push("recession");
  if (r.bleeding) cues.push("bleeding");
  if (r.suppuration === true) cues.push("suppuration");
  return cues;
}

/** Words for each cue; a cue is shown as words as well as a mark, and says nothing about a diagnosis. */
export const CUE_TEXT: Record<Cue, string> = {
  very_deep: `${VERY_DEEP_MM} mm or deeper`, deep: `${DEEP_MM} mm or deeper`, recession: "recession", bleeding: "bleeding", suppuration: "pus",
};

/** Plain text of the sweep, used to pin this order to the backend's. */
export const sweepText = (excluded?: ReadonlySet<string>) => sweep(excluded).map((s) => `${s.toothKey}${s.site}`).join(",");
export type { Site };
