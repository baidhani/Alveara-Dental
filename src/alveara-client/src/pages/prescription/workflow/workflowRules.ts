import type { PrescriptionProblem } from "../../../services/prescriptionApi";
import type { DraftView, PrescriptionStatus, WorkflowEntry } from "../../../services/prescriptionWorkflowApi";
import { parseDose } from "../PrescriptionForm";

/**
 * ALV-016-C01: the composer's boxes, and how they become the entry the server judges. The server's rules are authoritative; these only turn text into numbers so that a box that is not a number is put right
 * on the screen and nothing is sent for it (an empty box is "not given", never zero).
 */
export interface ComposerFields {
  medication: string; dose: string; unit: string; frequency: string; instructions: string; quantity: string; quantityUnit: string; refills: string; note: string;
}

export const EMPTY_FIELDS: ComposerFields = { medication: "", dose: "", unit: "", frequency: "", instructions: "", quantity: "", quantityUnit: "", refills: "", note: "" };

export const fieldsOf = (d: DraftView): ComposerFields => ({
  medication: d.medication, dose: String(d.doseValue), unit: d.doseUnit, frequency: d.frequency, instructions: d.instructions ?? "", quantity: d.quantityValue === null ? "" : String(d.quantityValue),
  quantityUnit: d.quantityUnit ?? "", refills: d.refills === null ? "" : String(d.refills), note: d.reviewNote ?? "",
});

/** The quantity box as a number: empty is "not given"; digits with up to two decimals are a number; anything else is a problem to put right. */
export function parseQuantity(text: string): { value: number | null; problem: PrescriptionProblem | null } {
  const t = text.trim();
  if (t === "") return { value: null, problem: null };
  if (!/^\d+(\.\d+)?$/.test(t)) return { value: null, problem: { field: "quantityValue", code: "not_a_number", message: "Enter the quantity as a number, for example 30 or 12.5." } };
  return { value: Number(t), problem: null };
}

/** The refills box as a whole number: empty is "not given" (0 means none); anything but digits is a problem to put right. */
export function parseRefills(text: string): { value: number | null; problem: PrescriptionProblem | null } {
  const t = text.trim();
  if (t === "") return { value: null, problem: null };
  if (!/^\d+$/.test(t)) return { value: null, problem: { field: "refills", code: "not_a_number", message: "Enter the number of refills as a whole number; 0 means none." } };
  return { value: Number(t), problem: null };
}

/** The entry to send, and any boxes that are not numbers (in which case nothing should be sent). */
export function entryOf(f: ComposerFields): { entry: WorkflowEntry; problems: PrescriptionProblem[] } {
  const dose = parseDose(f.dose);
  const quantity = parseQuantity(f.quantity);
  const refills = parseRefills(f.refills);
  return {
    entry: {
      medication: f.medication, doseValue: dose.value, doseUnit: f.unit, frequency: f.frequency, instructions: f.instructions.trim() === "" ? null : f.instructions,
      quantityValue: quantity.value, quantityUnit: f.quantityUnit === "" ? null : f.quantityUnit, refills: refills.value, reviewNote: f.note.trim() === "" ? null : f.note,
    },
    problems: [dose.problem, quantity.problem, refills.problem].filter((p): p is PrescriptionProblem => p !== null),
  };
}

export const quantityWords = (v: number | null, unit: string | null) => (v === null || unit === null ? null : `${v} ${unit}`);

/** What a prescription's status means, in words. */
export function statusWords(status: PrescriptionStatus): string {
  switch (status) {
    case "Cancelled": return "Cancelled: it should not be taken or dispensed.";
    case "Discontinued": return "Discontinued: it was stopped.";
    case "Superseded": return "Superseded: it was replaced by an amendment.";
    default: return "Active";
  }
}
