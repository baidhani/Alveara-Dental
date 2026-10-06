/**
 * STORY-013: what the diagnosis form knows about a valid entry, so a mistake is caught before anything is sent. A mirror of the server's DiagnosisRules, held to the same results by tests (the same cases
 * and the same codes): a required label (1 to 200 characters, one line), an optional FDI tooth, optional notes (up to 1,000 characters, may span lines) and an optional treatment-plan reference (trimmed,
 * runs of spaces collapsed, up to 100 characters, one line; absent means none and present-but-blank is refused). Only a convenience: the server judges every save again and its messages are the ones
 * shown if it disagrees. Every problem is reported together and the normalized value is returned only when there are none.
 */
import { ALL_KEYS } from "../odontogram/toothNumbering";

export const LABEL_MAX = 200;
export const NOTES_MAX = 1000;
export const REFERENCE_MAX = 100;

export interface DiagnosisProblem { field: string; code: string; message: string }
export interface DiagnosisEntry { encounterId: string; label: string; toothKey: string | null; notes: string | null; treatmentPlanReference: string | null }
export interface DiagnosisCheck { problems: DiagnosisProblem[]; value: DiagnosisEntry | null }

/** A control character: 0 to 31 and 127 to 159 (the same set as the server's `char.IsControl`). Line breaks and tabs are allowed in notes only. */
const isControl = (code: number) => code <= 0x1f || (code >= 0x7f && code <= 0x9f);
const hasControl = (value: string, allowLineBreaks = false) => [...value].some((c) => isControl(c.charCodeAt(0)) && !(allowLineBreaks && (c === "\n" || c === "\r" || c === "\t")));

/** One line of text: trimmed, runs of whitespace collapsed to one space. */
export const normalizeLine = (value: string) => value.trim().replace(/\s+/g, " ");
/** Text that may span lines: line endings made uniform and the ends trimmed. */
export const normalizeNotes = (value: string) => value.replace(/\r\n/g, "\n").replace(/\r/g, "\n").trim();

/** The form's fields as typed: an empty box is "not supplied" for the optional ones (null), and only an explicitly blank reference typed as spaces is refused. */
export interface DiagnosisDraft { encounterId: string; label: string; toothKey: string; notes: string; treatmentPlanReference: string }

export function checkDiagnosis(input: { encounterId: string | null; label: string | null; toothKey: string | null; notes: string | null; treatmentPlanReference: string | null }): DiagnosisCheck {
  const problems: DiagnosisProblem[] = [];
  if (!input.encounterId) problems.push({ field: "encounterId", code: "required", message: "Choose the encounter this diagnosis belongs to." });

  let label: string | null = null;
  if (input.label === null || input.label.trim() === "") problems.push({ field: "label", code: "required", message: "Enter the diagnosis." });
  else if (hasControl(input.label)) problems.push({ field: "label", code: "invalid_characters", message: "The diagnosis must be a single line of text, without line breaks or control characters." });
  else {
    label = normalizeLine(input.label);
    if (label.length > LABEL_MAX) { problems.push({ field: "label", code: "too_long", message: `The diagnosis can be at most ${LABEL_MAX} characters; this one has ${label.length}. Shorten it and put the detail in the notes.` }); label = null; }
  }

  let tooth: string | null = null;
  if (input.toothKey !== null) {
    if (!ALL_KEYS.includes(input.toothKey)) problems.push({ field: "toothKey", code: "unknown_tooth", message: `"${input.toothKey}" is not a tooth. Use the two-digit FDI number, such as 16 or 55, or leave the tooth empty.` });
    else tooth = input.toothKey;
  }

  let notes: string | null = null;
  if (input.notes !== null) {
    if (hasControl(input.notes, true)) problems.push({ field: "notes", code: "invalid_characters", message: "The notes contain a character that cannot be saved. Remove it and try again." });
    else {
      const n = normalizeNotes(input.notes);
      if (n.length > NOTES_MAX) problems.push({ field: "notes", code: "too_long", message: `The notes can be at most ${NOTES_MAX} characters; these have ${n.length}.` });
      else notes = n === "" ? null : n;
    }
  }

  let reference: string | null = null;
  if (input.treatmentPlanReference !== null) {
    if (hasControl(input.treatmentPlanReference)) problems.push({ field: "treatmentPlanReference", code: "invalid_characters", message: "The treatment-plan reference must be a single line of text, without line breaks or control characters." });
    else {
      const r = normalizeLine(input.treatmentPlanReference);
      if (r === "") problems.push({ field: "treatmentPlanReference", code: "blank", message: "The treatment-plan reference is blank. Enter the reference, or leave the field out if there is none." });
      else if (r.length > REFERENCE_MAX) problems.push({ field: "treatmentPlanReference", code: "too_long", message: `The treatment-plan reference can be at most ${REFERENCE_MAX} characters; this one has ${r.length}.` });
      else reference = r;
    }
  }

  return problems.length > 0 ? { problems, value: null } : { problems, value: { encounterId: input.encounterId!, label: label!, toothKey: tooth, notes, treatmentPlanReference: reference } };
}

/**
 * The form's draft as an entry to check: an empty tooth, notes or reference box means "not supplied". A reference box holding only spaces is NOT empty: it is sent so the rule can refuse it (someone
 * typed something), exactly as the server does.
 */
export function entryFromDraft(d: DiagnosisDraft) {
  return {
    encounterId: d.encounterId || null, label: d.label, toothKey: d.toothKey === "" ? null : d.toothKey,
    notes: d.notes === "" ? null : d.notes, treatmentPlanReference: d.treatmentPlanReference === "" ? null : d.treatmentPlanReference,
  };
}
