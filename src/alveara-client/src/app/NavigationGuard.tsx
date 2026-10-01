import { useEffect } from "react";
import { useBlocker } from "react-router-dom";
import { useAuth } from "../contexts/AuthContext";
import { useUnsavedChangesRegistry } from "../contexts/unsavedChangesStore";
import { DISCARD_PROMPT } from "../hooks/useUnsavedChangesWarning";

/**
 * ALV-N003 R02 (review finding ALV-N003-R01-03): asks before any in-app route change (shell links,
 * browser back/forward) discards unsaved edits; declining keeps the page and the draft exactly as
 * they were. Only a signed-in session is ever blocked: when authorization is lost (session expiry,
 * revocation) RequireAuth's redirect to sign-in must go through immediately and the protected UI -
 * draft included - must be cleared, never retained behind a confirmation prompt.
 */
export function NavigationGuard() {
  const unsaved = useUnsavedChangesRegistry();
  const { state } = useAuth();
  const shouldBlock = (unsaved?.anyDirty ?? false) && state.kind === "signed-in";

  const blocker = useBlocker(({ currentLocation, nextLocation }) => shouldBlock && currentLocation.pathname !== nextLocation.pathname);

  useEffect(() => {
    if (blocker.state !== "blocked") return;
    if (window.confirm(DISCARD_PROMPT)) blocker.proceed();
    else blocker.reset();
  }, [blocker]);

  return null;
}
