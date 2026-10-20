import type { LibraryFilter } from "../../services/documentsApi";

export type Draft = { categoryId: string; encounterId: string; toothKey: string; regionKey: string; from: string; to: string; text: string; status: string };
export const EMPTY_DRAFT: Draft = { categoryId: "", encounterId: "", toothKey: "", regionKey: "", from: "", to: "", text: "", status: "Active" };

/** The filters as the server takes them: only what was set, and the default status left out. */
export function toFilter(d: Draft): LibraryFilter {
  const filter: LibraryFilter = {};
  if (d.categoryId) filter.categoryId = d.categoryId;
  if (d.encounterId) filter.encounterId = d.encounterId;
  if (d.toothKey) filter.toothKey = d.toothKey;
  if (d.regionKey) filter.regionKey = d.regionKey;
  if (d.from) filter.from = d.from;
  if (d.to) filter.to = d.to;
  if (d.text.trim()) filter.text = d.text.trim();
  if (d.status !== "Active") filter.status = d.status as LibraryFilter["status"];
  return filter;
}

export type FilterErrors = Partial<Record<"category" | "encounter" | "toothKey" | "regionKey" | "from" | "to" | "text" | "status", string>>;
