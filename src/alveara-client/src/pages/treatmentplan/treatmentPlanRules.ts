/**
 * STORY-015: what the treatment plan form knows, so an obvious gap is caught before anything is sent. The server judges every save again (ownership of the diagnosis, whether the procedure is active and
 * fits the tooth, the database triggers) and its messages are the ones shown beside the fields. Pure functions only.
 */
import { isPrimary, isToothKey } from "../odontogram/toothNumbering";
import type { PlanItem, PlanItemInput, PlanningProcedure, TreatmentPlan } from "../../services/treatmentPlansApi";

/** STORY-008: the procedures "Review and complete" would offer: accepted, not withdrawn and not yet completed. */
export const acceptedOpenItems = (plan: TreatmentPlan): PlanItem[] =>
  plan.items.filter((i) => !i.isWithdrawn && i.acceptance === "Accepted" && i.completion !== "Completed");

/** A moment in the reader's own date and time format. */
export const when = (iso: string) => new Date(iso).toLocaleString();

/** A calendar day (no time of day) in the reader's own date format. */
export const dayOf = (isoDate: string) => new Date(`${isoDate}T00:00:00`).toLocaleDateString();

/** What the plan's derived acceptance status says, in words a person reads. */
export const answerWords = (status: string) => (status === "Accepted" ? "Accepted" : status === "Declined" ? "Declined" : status === "Partial" ? "Partly accepted" : "Not yet answered");

/** What a revision's change type means, in words a person reads. */
export function changeWords(changeType: string): string {
  switch (changeType) {
    case "Created": return "Plan created";
    case "ItemAdded": return "Procedure added";
    case "ItemWithdrawn": return "Procedure withdrawn";
    case "Renamed": return "Plan renamed";
    case "Revised": return "Plan revised";
    default: return changeType;
  }
}

/** A change in money with its sign spelled out, so it is never read as a total: "+$30.00", "-$30.00" or "no change". */
export function formatDelta(amount: number): string {
  if (amount === 0) return "no change";
  return `${amount > 0 ? "+" : "-"}${formatFee(Math.abs(amount))}`;
}

const quoted = (t: string | null) => `\u201C${t ?? "a plan of this patient"}\u201D`;

/** What happened to one old diagnosis reference, in words; a reference that stays unresolved says why. */
export function outcomeWords(i: { outcome: string; planTitle: string | null; reason: string | null }): string {
  switch (i.outcome) {
    case "Eligible": return `Can be tied to ${quoted(i.planTitle)}.`;
    case "Resolved": return `Tied to ${quoted(i.planTitle)}.`;
    case "AlreadyResolved": return `Already tied to ${quoted(i.planTitle)}.`;
    case "NoMatch": return `No matching plan. It stays unresolved. ${i.reason ?? ""}`.trim();
    case "Ambiguous": return `More than one plan matches. It stays unresolved. ${i.reason ?? ""}`.trim();
    default: return `Left as it is. ${i.reason ?? ""}`.trim();
  }
}

export const TITLE_MAX = 200;

export type SiteNeed = "none" | "tooth" | "toothAndSurface";

/** Where on the mouth a procedure is done, from what the catalog says it applies to: whole-mouth, arch and quadrant procedures take no tooth. */
export function siteNeedOf(scope: string): SiteNeed {
  if (scope === "Tooth") return "tooth";
  if (scope === "ToothSurface") return "toothAndSurface";
  return "none";
}

/** US dollars with cents, e.g. 1234.5 -> "$1,234.50". The currency is always US dollars today; the code is shown by the caller if it ever differs. */
export const formatFee = (amount: number) => amount.toLocaleString("en-US", { style: "currency", currency: "USD" });

/** The message for a field of the form, or undefined. Item fields are named as the server names them: `items[0].diagnosisId` for a new plan, `diagnosisId` for one added item. */
export const fieldMessage = (errors: Record<string, string>, field: string, prefix = ""): string | undefined => errors[prefix + field];

/** The messages the form has no field for (the plan-level and item-level ones, e.g. a duplicate procedure), in a stable order. */
export function otherMessages(errors: Record<string, string>, known: readonly string[]): string[] {
  return Object.entries(errors).filter(([field]) => !known.includes(field)).map(([, message]) => message);
}

export interface ItemDraft { diagnosisId: string; procedure: PlanningProcedure | null; toothKey: string; surface: string }
export const EMPTY_ITEM: ItemDraft = { diagnosisId: "", procedure: null, toothKey: "", surface: "" };

/** The first thing missing from an item as typed, or null when it is complete enough to send. */
export function itemGap(draft: ItemDraft): string | null {
  if (!draft.diagnosisId) return "Choose the diagnosis this procedure is for.";
  if (!draft.procedure) return "Choose the procedure to propose.";
  const need = siteNeedOf(draft.procedure.scope);
  if (need !== "none" && draft.toothKey.trim() === "") return "Choose the tooth.";
  if (need === "toothAndSurface" && draft.surface.trim() === "") return "Choose the surface.";
  if (need !== "none" && draft.toothKey.trim() !== "" && !isToothKey(draft.toothKey.trim())) return "That is not a tooth.";
  if (need !== "none" && isToothKey(draft.toothKey.trim())) {
    if (draft.procedure.dentition === "Permanent" && isPrimary(draft.toothKey.trim())) return "This procedure is for permanent teeth, and that is a primary tooth.";
    if (draft.procedure.dentition === "Primary" && !isPrimary(draft.toothKey.trim())) return "This procedure is for primary teeth, and that is a permanent tooth.";
  }
  return null;
}

/** The item as the server takes it. A tooth and surface are sent only where the procedure takes them, so a stale entry from a previously chosen procedure is never sent. */
export function toItemInput(draft: ItemDraft): PlanItemInput {
  const need = draft.procedure ? siteNeedOf(draft.procedure.scope) : "none";
  return {
    diagnosisId: draft.diagnosisId,
    procedureId: draft.procedure?.procedureId ?? "",
    toothKey: need === "none" ? null : draft.toothKey.trim() || null,
    surface: need === "toothAndSurface" ? draft.surface.trim().toUpperCase() || null : null,
  };
}

/** Whether the title is acceptable to send: required, one line, within the limit. The server states the exact rule if it disagrees. */
export function titleGap(title: string): string | null {
  const t = title.trim();
  if (t === "") return "Give the plan a title.";
  if (t.length > TITLE_MAX) return `The title can be at most ${TITLE_MAX} characters; this one has ${t.length}.`;
  return null;
}
