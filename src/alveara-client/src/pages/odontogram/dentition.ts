import { LOWER_PERMANENT, LOWER_PRIMARY, UPPER_PERMANENT, UPPER_PRIMARY } from "./toothNumbering";

/** Which teeth the chart draws. A view only: switching it never changes, hides or loses anything recorded. */
export type DentitionView = "Permanent" | "Primary" | "Mixed";
export const DENTITION_VIEWS: DentitionView[] = ["Permanent", "Primary", "Mixed"];

export interface Arch { title: string; keys: string[]; primary: boolean }

/** The arches each view draws, top to bottom. A mixed chart puts each arch's primary teeth beside its permanent ones, nearest the biting surface, so the two read together. */
export function archesFor(view: DentitionView): Arch[] {
  const perm = (title: string, keys: string[]): Arch => ({ title, keys, primary: false });
  const prim = (title: string, keys: string[]): Arch => ({ title, keys, primary: true });
  if (view === "Permanent") return [perm("Upper teeth", UPPER_PERMANENT), perm("Lower teeth", LOWER_PERMANENT)];
  if (view === "Primary") return [prim("Upper primary teeth", UPPER_PRIMARY), prim("Lower primary teeth", LOWER_PRIMARY)];
  return [perm("Upper permanent teeth", UPPER_PERMANENT), prim("Upper primary teeth", UPPER_PRIMARY), prim("Lower primary teeth", LOWER_PRIMARY), perm("Lower permanent teeth", LOWER_PERMANENT)];
}

export const keysInView = (view: DentitionView) => new Set(archesFor(view).flatMap((a) => a.keys));
