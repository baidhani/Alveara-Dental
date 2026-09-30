import { createContext, useCallback, useContext, useEffect, useState } from "react";
import type { ReactNode } from "react";
import { getMyPermissions, logout as apiLogout, setUnauthorizedHandler } from "../services/authApi";
import type { MyPermissions } from "../services/authApi";

export type AuthState =
  | { kind: "loading" }
  | { kind: "signed-out" }
  | { kind: "signed-in"; username: string; role: string; permissions: string[]; sessionExpiresAtUtc: string | null };

interface AuthContextValue {
  state: AuthState;
  /** Called after a successful login/MFA-challenge completion, so the shell reflects the new
   *  session immediately rather than waiting on the next permissions poll. */
  refresh: () => Promise<void>;
  hasPermission: (permission: string) => boolean;
  logout: () => Promise<void>;
  /** ALV-N009: true once a signed-in session's real server-issued expiry is within the warning
   *  window - lets the shell show a "your session will expire soon" notice before the user is
   *  suddenly signed out. Always false while loading/signed-out. */
  sessionExpiringSoon: boolean;
}

/** How long before the real session expiry to start warning - matches the shortest
 *  admin-configurable session timeout (5 minutes, see AccountService.MinSessionTimeoutMinutes)
 *  divided in half, so even the shortest possible session gets a meaningful warning window. */
const SESSION_EXPIRY_WARNING_MS = 2 * 60 * 1000;
const SESSION_EXPIRY_CHECK_INTERVAL_MS = 15 * 1000;
/** ALV-N009 R02: bounded revalidation poll for signed-in sessions - catches a role/permission
 *  change made elsewhere (another admin session) without waiting on an unrelated API call to
 *  happen to 401. Also backs the focus/visibility revalidation below. */
const SESSION_REVALIDATION_INTERVAL_MS = 30 * 1000;

const AuthContext = createContext<AuthContextValue | null>(null);

/**
 * ALV-001-C01: the app's single source of truth for "who is signed in and what can they do."
 * Deliberately re-derives session state from the server (GET /api/auth/permissions) rather than
 * trusting client-held state across a reload — the cookie is the only real credential, and
 * SecurityStamp-based revocation on the server can invalidate a session this client doesn't know
 * about yet, so every mount re-asks rather than assuming a previously-seen session still holds.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ kind: "loading" });
  const [sessionExpiringSoon, setSessionExpiringSoon] = useState(false);

  const refresh = useCallback(async () => {
    try {
      const result = await getMyPermissions();
      setSessionExpiringSoon(false);
      setState(
        result
          ? {
              kind: "signed-in",
              username: result.username,
              role: result.role,
              permissions: result.permissions,
              sessionExpiresAtUtc: result.sessionExpiresAtUtc,
            }
          : { kind: "signed-out" }
      );
    } catch {
      // Network failure, not an auth decision from the server (that's ApiError 401, already
      // handled inside getMyPermissions) — the DisconnectedBanner already communicates "can't
      // reach the server" separately, so this just falls back to signed-out rather than crashing.
      setState({ kind: "signed-out" });
    }
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  // ALV-N009 R02: revalidate a signed-in session's identity/permissions/role on a bounded poll and
  // whenever the tab regains focus or becomes visible again - catches a permission or role change
  // made elsewhere (e.g. another admin session) during this visit, rather than only discovering it
  // the next time an unrelated protected call happens to return 401. The server's rotated
  // SecurityStamp makes a stale cookie fail this call the moment it fires, so refresh() here both
  // picks up a widened/narrowed permission set and clears state if access was revoked outright.
  const isSignedIn = state.kind === "signed-in";
  useEffect(() => {
    if (!isSignedIn) return;
    const interval = setInterval(refresh, SESSION_REVALIDATION_INTERVAL_MS);
    const onFocusOrVisible = () => {
      if (document.visibilityState === "visible") refresh();
    };
    window.addEventListener("focus", onFocusOrVisible);
    document.addEventListener("visibilitychange", onFocusOrVisible);
    return () => {
      clearInterval(interval);
      window.removeEventListener("focus", onFocusOrVisible);
      document.removeEventListener("visibilitychange", onFocusOrVisible);
    };
  }, [isSignedIn, refresh]);

  // ALV-N009: react to a 401 from ANY authenticated call, not just the permissions check -
  // transitions straight to signed-out without another round-trip, since the 401 already proves
  // the session is gone. Registered/unregistered with this provider's own lifetime.
  useEffect(() => {
    setUnauthorizedHandler(() => {
      setSessionExpiringSoon(false);
      setState({ kind: "signed-out" });
    });
    return () => setUnauthorizedHandler(null);
  }, []);

  // ALV-N009: proactively warn before the real server-issued expiry arrives, rather than only
  // reacting after a request already failed. Polls a plain wall-clock comparison - no separate
  // request to the server - so it costs nothing beyond the timer itself, except at the boundary
  // itself (see below).
  //
  // ALV-N009 R02: once the authoritative expiry passes, re-ask the server via refresh() rather
  // than assuming signed-out locally. This is deliberate, not merely convenient: cookie auth here
  // uses sliding-expiration renewal, so a session the caller kept actively using may have already
  // been renewed server-side past the expiry timestamp this client captured at the last refresh.
  // refresh() reconciles against the current server truth either way - a genuinely expired cookie
  // 401s and this clears to signed-out; a transparently renewed one returns a fresh
  // sessionExpiresAtUtc and this effect re-arms against it.
  const sessionExpiresAtUtc = state.kind === "signed-in" ? state.sessionExpiresAtUtc : null;
  useEffect(() => {
    if (!sessionExpiresAtUtc) {
      setSessionExpiringSoon(false);
      return;
    }
    const expiresAtMs = new Date(sessionExpiresAtUtc).getTime();
    const check = () => {
      const remainingMs = expiresAtMs - Date.now();
      if (remainingMs <= 0) {
        refresh();
        return;
      }
      setSessionExpiringSoon(remainingMs <= SESSION_EXPIRY_WARNING_MS);
    };
    check();
    const interval = setInterval(check, SESSION_EXPIRY_CHECK_INTERVAL_MS);
    return () => clearInterval(interval);
  }, [sessionExpiresAtUtc, refresh]);

  const hasPermission = useCallback(
    (permission: string) =>
      state.kind === "signed-in" && Array.isArray(state.permissions) && state.permissions.includes(permission),
    [state]
  );

  const logout = useCallback(async () => {
    await apiLogout();
    setSessionExpiringSoon(false);
    setState({ kind: "signed-out" });
  }, []);

  return (
    <AuthContext.Provider value={{ state, refresh, hasPermission, logout, sessionExpiringSoon }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within an AuthProvider");
  return ctx;
}

export type { MyPermissions };
