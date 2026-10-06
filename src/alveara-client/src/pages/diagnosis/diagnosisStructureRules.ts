/**
 * ALV-013-C01: what the diagnosis form knows about the STRUCTURE of a diagnosis (coding, source, region), so a mistake is caught before anything is sent. A mirror of the server's DiagnosisRules, held to the
 * same results by tests (the same cases and the same codes). A diagnosis needs no coding; when it names a coding system it must give the code and the other way round; only the system's NAME is checked,
 * never the code against a code set (no terminology content is bundled). A source note belongs to an imported or mapped diagnosis; a region and a tooth exclude each other. Every problem is reported together.
 * Only a convenience: the server judges every save again and its messages are the ones shown if it disagrees.
 */
import { normalizeLine } from "./diagnosisRules";
import type { DiagnosisProblem } from "./diagnosisRules";

export const CODING_SYSTEMS = ["ICD-10-CM", "SNODENT", "Local"] as const;
export const SOURCES = ["Manual", "Imported", "Mapped"] as const;
export const REGIONS = ["FullMouth", "UpperArch", "LowerArch", "UpperRight", "UpperLeft", "LowerRight", "LowerLeft", "SoftTissue", "Tmj"] as const;
export const REGION_LABELS: Record<string, string> = {
  FullMouth: "Whole mouth", UpperArch: "Upper arch", LowerArch: "Lower arch", UpperRight: "Upper right quadrant", UpperLeft: "Upper left quadrant",
  LowerRight: "Lower right quadrant", LowerLeft: "Lower left quadrant", SoftTissue: "Soft tissue", Tmj: "Jaw joint (TMJ)",
};
export const CODE_MAX = 30;
export const SOURCE_NOTE_MAX = 200;

export interface DiagnosisStructure { codingSystem: string | null; code: string | null; source: string; sourceNote: string | null; regionKey: string | null }
export interface StructureInput { toothKey: string | null; codingSystem: string | null; code: string | null; source: string | null; sourceNote: string | null; regionKey: string | null }
export interface StructureCheck { problems: DiagnosisProblem[]; value: DiagnosisStructure | null }

const isCodeChar = (c: string) => /^[A-Za-z0-9._-]$/.test(c);
const hasControl = (value: string) => [...value].some((c) => c.charCodeAt(0) <= 0x1f || (c.charCodeAt(0) >= 0x7f && c.charCodeAt(0) <= 0x9f));

export function checkStructure(input: StructureInput): StructureCheck {
  const problems: DiagnosisProblem[] = [];
  let system: string | null = null;
  let code: string | null = null;
  const hasSystem = input.codingSystem !== null;
  const hasCode = input.code !== null;
  if (hasSystem) {
    if (!(CODING_SYSTEMS as readonly string[]).includes(input.codingSystem!)) problems.push({ field: "codingSystem", code: "unsupported_system", message: `"${input.codingSystem}" is not a supported coding system. Use one of ${CODING_SYSTEMS.join(", ")}, or leave the coding empty.` });
    else system = input.codingSystem;
  }
  if (hasCode) {
    const c = input.code!.trim();
    if (c === "") problems.push({ field: "code", code: "blank", message: "The code is blank. Enter the code, or leave the coding empty." });
    else if (c.length > CODE_MAX) problems.push({ field: "code", code: "too_long", message: `The code can be at most ${CODE_MAX} characters; this one has ${c.length}.` });
    else if (![...c].every(isCodeChar)) problems.push({ field: "code", code: "invalid_characters", message: "A code can contain only letters, digits, dots, hyphens and underscores, with no spaces." });
    else code = c;
  }
  if (hasSystem !== hasCode) problems.push({ field: hasSystem ? "code" : "codingSystem", code: "coding_incomplete", message: hasSystem ? "A coding system needs its code. Enter the code, or remove the system." : "A code needs its coding system. Choose the system, or remove the code." });

  let source = "Manual";
  if (input.source !== null) {
    if (!(SOURCES as readonly string[]).includes(input.source)) problems.push({ field: "source", code: "unsupported_source", message: `"${input.source}" is not a source. Use one of ${SOURCES.join(", ")}.` });
    else source = input.source;
  }

  let sourceNote: string | null = null;
  if (input.sourceNote !== null) {
    if (hasControl(input.sourceNote)) problems.push({ field: "sourceNote", code: "invalid_characters", message: "The source note must be a single line of text, without line breaks or control characters." });
    else {
      const n = normalizeLine(input.sourceNote);
      if (n === "") problems.push({ field: "sourceNote", code: "blank", message: "The source note is blank. Enter it, or leave it out." });
      else if (n.length > SOURCE_NOTE_MAX) problems.push({ field: "sourceNote", code: "too_long", message: `The source note can be at most ${SOURCE_NOTE_MAX} characters; this one has ${n.length}.` });
      else if (input.source === null || input.source === "Manual") problems.push({ field: "sourceNote", code: "source_required", message: "A source note says where an imported or mapped diagnosis came from; choose Imported or Mapped, or remove the note." });
      else sourceNote = n;
    }
  }

  let region: string | null = null;
  if (input.regionKey !== null) {
    if (!(REGIONS as readonly string[]).includes(input.regionKey)) problems.push({ field: "regionKey", code: "unknown_region", message: `"${input.regionKey}" is not an oral region. Use one of ${REGIONS.join(", ")}, or leave the region empty.` });
    else if (input.toothKey !== null) problems.push({ field: "regionKey", code: "conflicts_with_tooth", message: "A diagnosis is about one tooth or about a region, not both. Remove the tooth or the region." });
    else region = input.regionKey;
  }

  return problems.length > 0 ? { problems, value: null } : { problems, value: { codingSystem: system, code, source, sourceNote, regionKey: region } };
}

/** How a source reads: where the diagnosis came from, said in words and never by colour (null when it was entered here). */
export const sourceText = (source: string | undefined, note: string | null | undefined) =>
  source === "Imported" ? `Imported${note ? `: ${note}` : ""}` : source === "Mapped" ? `Mapped${note ? `: ${note}` : ""}` : null;

/** The structure boxes as typed: an empty box is "not supplied" (null), exactly as the server reads a missing field. */
export interface StructureDraft { codingSystem: string; code: string; source: string; sourceNote: string; regionKey: string }
export const EMPTY_STRUCTURE: StructureDraft = { codingSystem: "", code: "", source: "", sourceNote: "", regionKey: "" };

export function structureInputFromDraft(toothKey: string, d: StructureDraft): StructureInput {
  const none = (v: string) => (v === "" ? null : v);
  return { toothKey: none(toothKey), codingSystem: none(d.codingSystem), code: none(d.code), source: none(d.source), sourceNote: none(d.sourceNote), regionKey: none(d.regionKey) };
}
