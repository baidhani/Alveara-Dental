import { createContext, useCallback, useContext, useEffect, useState } from "react";
import type { ReactNode } from "react";
import { getMyPermissions, logout as apiLogout } from "../services/authApi";
import type { MyPermissions } from "../services/authApi";

export type AuthState =
  | { kind: "loading" }
  | { kind: "signed-out" }
  | { kind: "signed-in"; role: string; permissions: string[] };

interface AuthContextValue {
  state: AuthState;
  /** Called after a successful login/MFA-challenge completion, so the shell reflects the new
   *  session immediately rather than waiting on the next permissions poll. */
  refresh: () => Promise<void>;
  hasPermission: (permission: string) => boolean;
  logout: () => Promise<void>;
}

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

  const refresh = useCallback(async () => {
    try {
      const result = await getMyPermissions();
      setState(result ? { kind: "signed-in", role: result.role, permissions: result.permissions } : { kind: "signed-out" });
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

  const hasPermission = useCallback(
    (permission: string) => state.kind === "signed-in" && state.permissions.includes(permission),
    [state]
  );

  const logout = useCallback(async () => {
    await apiLogout();
    setState({ kind: "signed-out" });
  }, []);

  return (
    <AuthContext.Provider value={{ state, refresh, hasPermission, logout }}>{children}</AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within an AuthProvider");
  return ctx;
}

export type { MyPermissions };
