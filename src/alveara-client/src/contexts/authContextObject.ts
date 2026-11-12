import { createContext, useContext } from "react";

/** ALV-N020 R02: the auth context object and its types, apart from AuthContext.tsx, so that file exports only components (fast refresh) and the identity
 *  counter can be read by identity-scoped providers (the patient context) without importing the provider module. */
export type AuthState =
  | { kind: "loading" }
  | { kind: "signed-out" }
  | { kind: "signed-in"; username: string; role: string; permissions: string[]; sessionExpiresAtUtc: string | null };

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
}

export const AuthContext = createContext<AuthContextValue | null>(null);

/** ALV-N020 (F-037): the identity counter for identity-scoped state. Outside an AuthProvider (an isolated component test) it is a constant 0. */
export function useIdentityEpoch(): number {
  return useContext(AuthContext)?.identityEpoch ?? 0;
}
