import { ApiError, isServerUnreachable } from "../services/authApi";

/** ALV-N020: the sign-in page's outcome states and the two rules behind them, kept out of the component file. */
export type LoginFormState =
  | { kind: "idle" }
  | { kind: "submitting" }
  | { kind: "invalid-credentials" }
  | { kind: "account-disabled" }
  | { kind: "locked-out"; lockedUntilUtc?: string }
  | { kind: "rate-limited" }
  | { kind: "server-unavailable" }
  | { kind: "server-problem" }
  | { kind: "unexpected-error" };

/** Where a successful sign-in goes: the page recorded for THIS user's expired session, otherwise the dashboard (ALV-N020, F-037). */
export function destinationAfterSignIn(signedInUsername: string, from: { pathname: string; search: string } | undefined, fromUser: string | undefined): string {
  return from && fromUser && fromUser === signedInUsername ? `${from.pathname}${from.search}` : "/";
}

/** ALV-N020 (F-014): what a failed sign-in means. Only the server's own 401 is "wrong username or password". */
export function classifyLoginFailure(err: unknown): LoginFormState {
  if (err instanceof ApiError) {
    if (err.status === 429) return { kind: "rate-limited" };
    if (err.code === "account_disabled") return { kind: "account-disabled" };
    if (err.code === "account_locked") return { kind: "locked-out", lockedUntilUtc: err.body.lockedUntilUtc as string | undefined };
    if (isServerUnreachable(err)) return { kind: "server-unavailable" };
    if (err.status >= 500) return { kind: "server-problem" };
    return { kind: "invalid-credentials" };
  }
  const name = typeof err === "object" && err !== null ? (err as { name?: unknown }).name : undefined;    // a DOMException (abort) is not an Error everywhere
  if (name === "AbortError" || name === "TypeError") return { kind: "server-unavailable" };                        // timeout or no connection
  return { kind: "unexpected-error" };
}

/**
 * ALV-N021 A3 (inventory row 3): the lockout is shown on the sign-in page, before any session exists, so there is no practice zone to show a clock time
 * in. It is shown as a duration instead ("Try again in 12 minutes."), from the server's instant and the elapsed time on this device only.
 */
export function tryAgainIn(lockedUntilUtc: string, nowMs: number = Date.now()): string {
  const until = Date.parse(lockedUntilUtc);
  if (Number.isNaN(until)) return "Try again later.";
  const minutes = Math.max(1, Math.ceil((until - nowMs) / 60_000));
  return `Try again in ${minutes} ${minutes === 1 ? "minute" : "minutes"}.`;
}
