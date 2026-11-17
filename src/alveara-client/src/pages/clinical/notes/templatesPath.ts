/**
 * ALV-N022 A3 (Q-5j): the note templates belong to the practice, so their page lives at /clinical/templates, not under a patient. When it is opened from a patient's
 * clinical tab, that patient's id rides along as ?from= so the page can offer the way back (a query string, so it survives a reload).
 */
export const TEMPLATES_PATH = "/clinical/templates";
const ID = /^[0-9a-fA-F-]{36}$/;

export const templatesPath = (fromPatientId?: string): string => (fromPatientId && ID.test(fromPatientId) ? `${TEMPLATES_PATH}?from=${fromPatientId}` : TEMPLATES_PATH);

/** The patient clinical tab to go back to, or null when the page was not opened from one (or the value is not a patient id). */
export function backToPatient(search: string): string | null {
  const from = new URLSearchParams(search).get("from");
  return from && ID.test(from) ? `/patients/${from}/clinical` : null;
}
