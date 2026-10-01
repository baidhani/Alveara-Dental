import { useCallback, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { UnsavedChangesContext } from "./unsavedChangesStore";

/**
 * ALV-N003 R02 (review finding ALV-N003-R01-03): the app-wide registry of unsaved edits. Editors
 * report into it through `useUnsavedChangesWarning`; the router-level `NavigationGuard` reads it so
 * shell links and browser back/forward ask before discarding a draft. An editor that unmounts
 * (including because the session ended) clears its own entry, so a stale flag can never keep
 * blocking navigation or keep protected UI alive.
 */
export function UnsavedChangesProvider({ children }: { children: ReactNode }) {
  const [dirtyKeys, setDirtyKeys] = useState<ReadonlySet<string>>(new Set());

  const register = useCallback((key: string, dirty: boolean) => {
    setDirtyKeys((prev) => {
      if (prev.has(key) === dirty) return prev;
      const next = new Set(prev);
      if (dirty) next.add(key);
      else next.delete(key);
      return next;
    });
  }, []);

  const value = useMemo(() => ({ anyDirty: dirtyKeys.size > 0, register }), [dirtyKeys, register]);
  return <UnsavedChangesContext.Provider value={value}>{children}</UnsavedChangesContext.Provider>;
}
