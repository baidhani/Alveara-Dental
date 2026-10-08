import type { ProcedureInput, ProcedureVersion } from "../../services/proceduresApi";

export const CODE_SYSTEM_TEXT: Record<string, string> = { Local: "Practice code", CDT: "CDT code", External: "External code set" };
export const CATEGORY_TEXT: Record<string, string> = {
  Diagnostic: "Diagnostic", Preventive: "Preventive", Restorative: "Restorative", Endodontic: "Endodontic", Periodontic: "Periodontic", Prosthodontic: "Prosthodontic",
  OralSurgery: "Oral surgery", Orthodontic: "Orthodontic", Adjunctive: "Adjunctive", Other: "Other",
};
export const SCOPE_TEXT: Record<string, string> = { WholeMouth: "Whole mouth", Arch: "One arch", Quadrant: "One quadrant", Tooth: "One tooth", ToothSurface: "One tooth surface" };
export const DENTITION_TEXT: Record<string, string> = { Permanent: "Permanent teeth", Primary: "Primary teeth", Both: "Permanent and primary teeth" };
export const STATUS_TEXT: Record<string, string> = { Active: "Active", Inactive: "Inactive", Scheduled: "Scheduled", Expired: "Expired" };

const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
export const formatFee = (fee: number) => money.format(fee);
export const isToothLevel = (scope: string) => scope === "Tooth" || scope === "ToothSurface";
export const when = (iso: string) => new Date(iso).toLocaleString();
/** A calendar date written the way the server holds it (no time zone shift): 2030-03-15 becomes "Mar 15, 2030". */
export const dateText = (d: string) => new Date(`${d}T00:00:00`).toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" });

export const emptyInput = (): ProcedureInput => ({
  codeSystem: "Local", code: "", description: "", category: "", scope: "", dentition: "Both", fee: null, sourceName: "", sourceVersion: "", effectiveFrom: "", validThrough: "",
});

/** The form's starting values for changing a procedure: its newest version, with the start date left blank (blank means today). */
export const inputOf = (codeSystem: string, code: string, v: ProcedureVersion): ProcedureInput => ({
  codeSystem, code, description: v.description, category: v.category, scope: v.scope, dentition: v.dentition, fee: v.fee,
  sourceName: v.sourceName ?? "", sourceVersion: v.sourceVersion ?? "", effectiveFrom: "", validThrough: v.validThrough ?? "",
});

/** What a person can see about where a version applies: "One tooth (permanent teeth)". */
export const appliesText = (v: ProcedureVersion) => `${SCOPE_TEXT[v.scope] ?? v.scope}${isToothLevel(v.scope) ? ` (${DENTITION_TEXT[v.dentition]?.toLowerCase() ?? v.dentition})` : ""}`;

/** Checks the fee text before sending; the server checks again. Returns the number, or a message. */
export function parseFee(text: string): { fee: number } | { error: string } {
  const t = text.trim().replace(/^\$/, "").replace(/,/g, "");
  if (t === "") return { error: "A fee is required (enter 0 for no charge)." };
  if (!/^\d+(\.\d{1,2})?$/.test(t)) return { error: "Enter an amount in dollars with at most two decimals, such as 95 or 95.50." };
  const fee = Number(t);
  if (fee > 1_000_000) return { error: "A fee cannot be more than $1,000,000.00." };
  return { fee };
}
