import { useEffect, useId } from "react";
import { useUnsavedChangesRegistry } from "../contexts/unsavedChangesStore";

export const DISCARD_PROMPT = "You have unsaved changes. Discard them?";

/**
 * ALV-N003 unsaved-change protection. While `dirty`:
 * - closing the tab or reloading triggers the browser's native "leave site?" prompt
 *   (`beforeunload`), and
 * - the editor is registered with the app-wide unsaved-changes registry (R02), which the
 *   router-level NavigationGuard reads so shell links and back/forward also ask first.
 * In-panel navigation (cancel, switching rows/tabs) is guarded separately with `confirmDiscard`.
 */
export function useUnsavedChangesWarning(dirty: boolean) {
  const key = useId();
  const register = useUnsavedChangesRegistry()?.register;

  useEffect(() => {
    register?.(key, dirty);
    return () => register?.(key, false); // an unmounted editor never keeps blocking navigation
  }, [dirty, key, register]);

  useEffect(() => {
    if (!dirty) return;
    const handler = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [dirty]);
}

/** True if there is nothing to lose, or the user explicitly agrees to discard it. */
export function confirmDiscard(dirty: boolean): boolean {
  return !dirty || window.confirm(DISCARD_PROMPT);
}
