import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { useAuth } from "../contexts/AuthContext";
import { LoadingState } from "../components/StatePatterns";
import { PermissionDenied } from "../components/PermissionDenied";

/**
 * ALV-N009: the router-level authorization boundary. Neither of these components is itself the
 * security authority - the server always re-checks via [RequirePermission] regardless of what
 * renders here - they exist so a direct/deep-link URL never mounts a protected page's own data
 * fetch before the caller is known to be allowed to see it, and so every route gets the same
 * denial behavior instead of each page inventing its own (MfaSettingsPage's older ad hoc
 * `state.kind === "signed-out"` redirect is exactly the duplication this centralizes).
 */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { state, endedReason, expiredUsername } = useAuth();
  const location = useLocation();

  if (state.kind === "loading") {
    return <LoadingState label="Checking your session…" />;
  }
  if (state.kind === "signed-out") {
    // ALV-N020 (F-007, F-037): "Your session expired" only when a session really ended here by expiry or revocation; a first visit (or a deep link with no
    // session) gets the plain sign-in page. The page the person was on is carried back ONLY for that same person after an expiry, only in this navigation's
    // in-memory state (never in storage); an explicit sign-out or a first visit carries nothing, so a different person always lands on the dashboard.
    if (endedReason === "expired" && expiredUsername) {
      return <Navigate to="/login?reason=expired" replace state={{ from: location, fromUser: expiredUsername }} />;
    }
    return <Navigate to="/login" replace />;
  }
  return <>{children}</>;
}

/** Denies access to the wrapped route when the signed-in caller lacks `permission`, rendering the
 *  same PermissionDenied state a page's own data fetch would show on a 403 - but before any
 *  protected data fetch for this route ever starts. */
/** ALV-N018: like <see cref="RequirePermission"/> but any one of the listed permissions is enough (the account list opens for a holder of account status or session revocation). */
export function RequireAnyPermission({ permissions, shown, children }: { permissions: string[]; shown?: string; children: ReactNode }) {
  const { hasPermission } = useAuth();

  if (!permissions.some((p) => hasPermission(p))) {
    return <PermissionDenied requiredPermission={shown ?? permissions[0]} />;
  }
  return <>{children}</>;
}

export function RequirePermission({ permission, children }: { permission: string; children: ReactNode }) {
  const { hasPermission } = useAuth();

  if (!hasPermission(permission)) {
    return <PermissionDenied requiredPermission={permission} />;
  }
  return <>{children}</>;
}
