/**
 * STORY-006: the three tooth-numbering systems, display only. The stored identity of a tooth is its FDI / ISO 3950 key (the first digit the quadrant, the second the position from the
 * midline); Universal (1-32, A-T) and Palmer (UR1, LLE...) are only ways of showing it. This mirrors the backend's ToothNumbering and is held to the same reference teeth by the tests.
 * Which system a practice or a user sees is a presentation preference that is not built yet: the chart takes the system as a parameter and defaults to Universal.
 */
export type NumberingSystem = "Universal" | "Fdi" | "Palmer";
export const DEFAULT_NUMBERING: NumberingSystem = "Universal";

const range = (from: number, to: number) => Array.from({ length: Math.abs(to - from) + 1 }, (_, i) => (from <= to ? from + i : from - i));

/** The 32 permanent teeth as a dentist looks at the patient: upper arch left to right is the patient's right to left, so 18..11 then 21..28; the lower arch 48..41 then 31..38. */
export const UPPER_PERMANENT = [...range(8, 1).map((p) => `1${p}`), ...range(1, 8).map((p) => `2${p}`)];
export const LOWER_PERMANENT = [...range(8, 1).map((p) => `4${p}`), ...range(1, 8).map((p) => `3${p}`)];
const PRIMARY_QUADRANTS = [5, 6, 7, 8];
/** The primary teeth as a dentist looks at the patient: the upper arch is 55..51 then 61..65, the lower 85..81 then 71..75. */
export const UPPER_PRIMARY = [...range(5, 1).map((p) => `5${p}`), ...range(1, 5).map((p) => `6${p}`)];
export const LOWER_PRIMARY = [...range(5, 1).map((p) => `8${p}`), ...range(1, 5).map((p) => `7${p}`)];

export const PRIMARY_KEYS = PRIMARY_QUADRANTS.flatMap((q) => range(1, 5).map((p) => `${q}${p}`));
export const ALL_KEYS = [...UPPER_PERMANENT, ...LOWER_PERMANENT, ...PRIMARY_KEYS];
const VALID = new Set(ALL_KEYS);

export const isToothKey = (key: string | null | undefined): key is string => key != null && VALID.has(key);
export const isPrimary = (key: string) => key.charCodeAt(0) >= 53; // quadrants 5-8
const split = (key: string) => ({ quadrant: key.charCodeAt(0) - 48, position: key.charCodeAt(1) - 48 });
export const isAnterior = (key: string) => split(key).position <= 3;

const PALMER_QUADRANTS = ["", "UR", "UL", "LL", "LR"];

function universalOf(key: string): string {
  const { quadrant, position } = split(key);
  if (quadrant === 1) return String(9 - position);
  if (quadrant === 2) return String(8 + position);
  if (quadrant === 3) return String(25 - position);
  if (quadrant === 4) return String(24 + position);
  const letter = (code: number) => String.fromCharCode(code);
  if (quadrant === 5) return letter(65 + 5 - position); // 55 -> A ... 51 -> E
  if (quadrant === 6) return letter(70 + position - 1); // 61 -> F ... 65 -> J
  if (quadrant === 7) return letter(75 + 5 - position); // 75 -> K ... 71 -> O
  return letter(80 + position - 1); // 81 -> P ... 85 -> T
}

/** How the tooth reads in the chosen system. Throws for anything that is not one of the 52 keys: a wrong key is a defect to surface, never something to show as a blank. */
export function displayTooth(key: string, system: NumberingSystem = DEFAULT_NUMBERING): string {
  if (!isToothKey(key)) throw new Error(`Not a tooth key: "${key}"`);
  if (system === "Fdi") return key;
  if (system === "Universal") return universalOf(key);
  const { quadrant, position } = split(key);
  return PALMER_QUADRANTS[(quadrant - 1) % 4 + 1] + (isPrimary(key) ? String.fromCharCode(64 + position) : String(position));
}

const FROM_UNIVERSAL = new Map(ALL_KEYS.map((k) => [universalOf(k), k]));

/** Reads what a person typed and returns the stored key, or null when it is not exactly a tooth in that system (a typo is refused, never guessed at). */
export function parseTooth(text: string | null | undefined, system: NumberingSystem = DEFAULT_NUMBERING): string | null {
  const t = text?.trim();
  if (!t) return null;
  if (system === "Fdi") return isToothKey(t) ? t : null;
  if (system === "Universal") return FROM_UNIVERSAL.get(t.toUpperCase()) ?? null;
  const upper = t.toUpperCase();
  if (upper.length !== 3) return null;
  const q = PALMER_QUADRANTS.indexOf(upper.slice(0, 2));
  if (q < 1) return null;
  const c = upper[2];
  const key = c >= "1" && c <= "8" ? `${q}${c}` : c >= "A" && c <= "E" ? `${q + 4}${c.charCodeAt(0) - 64}` : null;
  return isToothKey(key) ? key : null;
}

export const SURFACE_NAMES: Record<string, string> = { M: "Mesial", D: "Distal", L: "Lingual", B: "Buccal", F: "Facial", O: "Occlusal", I: "Incisal" };

/** The surfaces that exist on this tooth: Occlusal and Buccal on posterior teeth, Incisal and Facial on anterior ones; Mesial, Distal and Lingual on every tooth. */
export const surfacesFor = (key: string): string[] => (isAnterior(key) ? ["M", "I", "D", "F", "L"] : ["M", "O", "D", "B", "L"]);

const QUADRANT_NAMES = ["", "upper right", "upper left", "lower left", "lower right"];
const POSITION_NAMES = ["", "central incisor", "lateral incisor", "canine", "first premolar", "second premolar", "first molar", "second molar", "third molar"];
const PRIMARY_POSITION_NAMES = ["", "central incisor", "lateral incisor", "canine", "first molar", "second molar"];

/** A plain-words name for the tooth ("upper right third molar") for assistive technology and the detail heading; the same whatever system is displayed. */
export function toothName(key: string): string {
  const { quadrant, position } = split(key);
  const place = QUADRANT_NAMES[(quadrant - 1) % 4 + 1];
  return `${place} ${isPrimary(key) ? "primary " : ""}${(isPrimary(key) ? PRIMARY_POSITION_NAMES : POSITION_NAMES)[position]}`;
}
