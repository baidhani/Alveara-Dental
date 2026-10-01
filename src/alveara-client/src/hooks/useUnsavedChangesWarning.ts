import { useEffect } from "react";

export const DISCARD_PROMPT = "You have unsaved changes. Discard them?";

/**
 * ALV-N003 unsaved-change protection, browser half: while `dirty`, closing the tab or reloading
 * triggers the browser's native "leave site?" prompt. In-app navigation (cancel, switching rows or
 * tabs) is guarded separately with `confirmDiscard`, since the browser event never fires for it.
 */
export function useUnsavedChangesWarning(dirty: boolean) {
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
