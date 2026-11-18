import type { BookableTreatment } from "../../services/schedulingApi";
import { DEFAULT_NUMBERING, displayTooth } from "../odontogram/toothNumbering";

/** How one accepted procedure reads in the booking form: its code, description, tooth and the plan's reference - no plan title or diagnosis. */
export function bookableLabel(t: BookableTreatment): string {
  const where = t.toothKey ? ` (tooth ${displayTooth(t.toothKey, DEFAULT_NUMBERING)}${t.surface ? `, ${t.surface}` : ""})` : "";
  return `${t.procedureCode} ${t.procedureDescription}${where} - plan ${t.planReference ?? "without a reference code"}, item ${t.itemNumber}`;
}
