import { MATERIAL_MAX, NOTE_MAX, REASON_MAX, SHADE_MAX, TITLE_MAX } from "../../services/externalCasesApi";
import type { CaseKind } from "../../services/externalCasesApi";

/** The values of a case form as text, so a half-typed value is never lost. */
export interface CaseFormValues { summary: string; dueOn: string; notes: string; toothKey: string; material: string; shade: string }

export const emptyValues = (): CaseFormValues => ({ summary: "", dueOn: "", notes: "", toothKey: "", material: "", shade: "" });

export const valuesOf = (c: { summary: string; dueOn: string | null; notes: string | null; toothKey: string | null; material: string | null; shade: string | null }): CaseFormValues =>
  ({ summary: c.summary, dueOn: c.dueOn ?? "", notes: c.notes ?? "", toothKey: c.toothKey ?? "", material: c.material ?? "", shade: c.shade ?? "" });

/** What is wrong with the form, by field, before anything is sent. The server checks the same limits again. */
export function checkValues(kind: CaseKind, v: CaseFormValues, partyId: string): Record<string, string> {
  const e: Record<string, string> = {};
  if (!partyId) e.party = kind === "Lab" ? "Choose the laboratory." : "Choose who the patient is being referred to.";
  const summary = v.summary.trim();
  const max = kind === "Lab" ? TITLE_MAX : REASON_MAX;
  if (summary === "" || summary.length > max) e.summary = kind === "Lab" ? `Write what is being made (up to ${TITLE_MAX} characters).` : `Write the reason for the referral (up to ${REASON_MAX} characters).`;
  if (v.notes.trim().length > NOTE_MAX) e.notes = `Keep the notes to ${NOTE_MAX} characters.`;
  if (kind === "Lab") {
    if (v.toothKey.trim() !== "" && !/^\d{2}$/.test(v.toothKey.trim())) e.toothKey = "Enter the tooth as its two-digit number, or leave it blank.";
    if (v.material.trim().length > MATERIAL_MAX) e.material = `Keep the material to ${MATERIAL_MAX} characters.`;
    if (v.shade.trim().length > SHADE_MAX) e.shade = `Keep the shade to ${SHADE_MAX} characters.`;
  }
  return e;
}
