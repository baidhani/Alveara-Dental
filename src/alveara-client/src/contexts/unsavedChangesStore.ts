import { createContext, useContext } from "react";

export interface UnsavedChangesValue {
  /** True while any mounted editor reports unsaved edits. */
  anyDirty: boolean;
  /** An editor reports (or clears) its own dirty state under a stable key. */
  register: (key: string, dirty: boolean) => void;
}

export const UnsavedChangesContext = createContext<UnsavedChangesValue | null>(null);

/** The registry, or null when rendered outside the provider (isolated component tests). */
export function useUnsavedChangesRegistry(): UnsavedChangesValue | null {
  return useContext(UnsavedChangesContext);
}
