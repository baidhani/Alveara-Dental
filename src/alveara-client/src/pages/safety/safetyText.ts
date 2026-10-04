import type { SafetySummary } from "../../services/safetyApi";

export const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;
export const when = (iso: string) => new Date(iso).toLocaleString();
export const day = (iso: string) => new Date(iso).toLocaleDateString();

/** The one-line headline, in words: what is recorded, never what is merely absent. */
export function safetyHeadline(s: SafetySummary): string {
  const parts = [
    s.activeAlertCount > 0 && `${plural(s.activeAlertCount, "active alert", "active alerts")}${s.highestSeverity ? ` (highest: ${s.highestSeverity})` : ""}`,
    s.activeAllergyCount > 0 && plural(s.activeAllergyCount, "active allergy", "active allergies"),
    s.currentMedicationCount > 0 && plural(s.currentMedicationCount, "current medication", "current medications"),
    s.openClearanceCount > 0 && `${plural(s.openClearanceCount, "clearance", "clearances")} open`,
  ].filter(Boolean);
  return parts.length > 0 ? parts.join(" · ") : "No alerts, active allergies or current medications are recorded.";
}
