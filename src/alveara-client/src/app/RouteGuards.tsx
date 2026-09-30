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
  const { state } = useAuth();
  const location = useLocation();

  if (state.kind === "loading") {
    return <LoadingState label="Checking your session…" />;
  }
  if (state.kind === "signed-out") {
    // Matches the existing `?reason=expired` convention LoginPage already handles (see
    // MfaSettingsPage's prior ad hoc use of the same query string) - a direct/deep link to a
    // protected route with no valid session reads the same as a session that expired mid-visit,
    // since from the caller's perspective the result (denied, sign in again) is identical.
    return <Navigate to="/login?reason=expired" replace state={{ from: location }} />;
  }
  return <>{children}</>;
}

/** Denies access to the wrapped route when the signed-in caller lacks `permission`, rendering the
 *  same PermissionDenied state a page's own data fetch would show on a 403 - but before any
 *  protected data fetch for this route ever starts. */
export function RequirePermission({ permission, children }: { permission: string; children: ReactNode }) {
  const { hasPermission } = useAuth();

  if (!hasPermission(permission)) {
    return <PermissionDenied requiredPermission={permission} />;
  }
  return <>{children}</>;
}
