import { createContext, useContext } from "react";

/** ALV-N020 R02: the auth context object and its types, apart from AuthContext.tsx, so that file exports only components (fast refresh) and the identity
 *  counter can be read by identity-scoped providers (the patient context) without importing the provider module. */
export type AuthState =
  | { kind: "loading" }
  | { kind: "signed-out" }
  | { kind: "signed-in"; username: string; role: string; permissions: string[]; sessionExpiresAtUtc: string | null;
      /** ALV-N021 A3: the practice zone from the newest authenticated session response, or null when it is missing or this browser cannot use it. */
      practiceTimeZoneId: string | null;
      /** ALV-N030: the account must choose its own password first (temporary password); every route but the change page sends it there. */
      mustChangePassword?: boolean;
      /** ALV-N030: "Staff" or "DeploymentAdministrator" (the account-only first-run administrator). */
      accountKind?: string;
      /** ALV-N030: this account's staff member is the Practice Owner; and whether the practice has one at all. */
      isPracticeOwner?: boolean;
      practiceOwnerExists?: boolean };

export interface AuthContextValue {
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
  /** ALV-N020 (F-155): the last session check could not reach the server or got an error that is not an authentication decision (network, timeout, any
   *  5xx). The signed-in shell, and every unsaved form in it, stays as it is; the check is retried until it gets an answer. Only a 401 ends the session. */
  connectionLost: boolean;
  /** ALV-N020 (F-007): why the session in THIS page lifetime ended - "expired" (a 401, expiry or revocation discovered by the server) or "signed-out" (the
   *  person signed out). null when no session has ended here (a first visit), so the sign-in page can stay neutral. */
  endedReason: "expired" | "signed-out" | null;
  /** ALV-N020 (F-037): the username of the session that EXPIRED here, kept only in memory, so a same-user return after expiry can go back to the page they
   *  were on. Cleared by an explicit sign-out and replaced by any other identity. */
  expiredUsername: string | null;
  /** ALV-N020 (F-037): increases whenever the signed-in identity ends or changes (sign-out, 401, a different user found by revalidation). Anything that holds
   *  identity-scoped state (the patient in context) clears itself when it changes. */
  identityEpoch: number;
  /** ALV-N021 A3 (briefing 4.1-5): set when a newer authenticated response in THIS session carries a different usable practice zone than the one in use
   *  (a reconnect after a redeployment, or an inconsistent server); the screens have already re-rendered in it. null otherwise. */
  practiceZoneChangedTo: string | null;
  dismissPracticeZoneChange: () => void;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

/** ALV-N020 (F-037): the identity counter for identity-scoped state. Outside an AuthProvider (an isolated component test) it is a constant 0. */
export function useIdentityEpoch(): number {
  return useContext(AuthContext)?.identityEpoch ?? 0;
}

/** ALV-N021 A3: the practice zone to render instants in, or null (signed out, outside an AuthProvider, or missing/unusable): never the browser's zone. */
export function usePracticeZone(): string | null {
  const state = useContext(AuthContext)?.state;
  return state?.kind === "signed-in" ? state.practiceTimeZoneId : null;
}
