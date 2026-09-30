import { useState } from "react";
import type { FormEvent } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { PageHeader } from "../components/PageHeader";
import { FormField } from "../components/FormField";
import { Button } from "../components/Button";
import { ApiError, login } from "../services/authApi";
import { useAuth } from "../contexts/AuthContext";
import "./LoginPage.css";

type LoginFormState =
  | { kind: "idle" }
  | { kind: "submitting" }
  | { kind: "invalid-credentials" }
  | { kind: "account-disabled" }
  | { kind: "locked-out"; lockedUntilUtc?: string }
  | { kind: "rate-limited" }
  | { kind: "unexpected-error" };

/**
 * ALV-001-C01's login, lockout, and expired-session screens combined into one page: lockout and
 * disabled-account are distinct, honestly-worded failure states (never a generic "login failed"),
 * and a session that expired elsewhere in the app lands back here with an explanatory banner
 * (see the `?reason=expired` handling) rather than a silent redirect that leaves the user
 * guessing why they were signed out.
 */
export function LoginPage() {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [formState, setFormState] = useState<LoginFormState>({ kind: "idle" });
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const location = useLocation();
  const { refresh } = useAuth();

  // ALV-N009: RequireAuth records where a direct/deep-link caller was denied (see
  // RouteGuards.tsx) so a successful sign-in returns them there instead of always to "/" -
  // `state.from` is only ever a location object this app itself set, never attacker-controlled
  // input, so using it directly to navigate is safe.
  const from = (location.state as { from?: { pathname: string; search: string } } | null)?.from;
  const redirectTo = from ? `${from.pathname}${from.search}` : "/";

  const sessionExpired = searchParams.get("reason") === "expired";

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setFormState({ kind: "submitting" });

    try {
      const result = await login(username, password);
      if ("mfaRequired" in result) {
        navigate("/mfa-challenge", { state: { challengeToken: result.challengeToken } });
        return;
      }
      await refresh();
      navigate(redirectTo, { replace: true });
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 429) {
          setFormState({ kind: "rate-limited" });
        } else if (err.code === "account_disabled") {
          setFormState({ kind: "account-disabled" });
        } else if (err.code === "account_locked") {
          setFormState({ kind: "locked-out", lockedUntilUtc: err.body.lockedUntilUtc as string | undefined });
        } else {
          setFormState({ kind: "invalid-credentials" });
        }
      } else {
        setFormState({ kind: "unexpected-error" });
      }
    }
  }

  const submitting = formState.kind === "submitting";

  return (
    <div className="alv-login">
      <PageHeader title="Sign in" description="Enter your username and password to access Alveara Dental." />

      {sessionExpired && (
        <div className="alv-login__banner alv-login__banner--warning" role="alert">
          Your session expired. Please sign in again.
        </div>
      )}

      {formState.kind === "invalid-credentials" && (
        <div className="alv-login__banner alv-login__banner--danger" role="alert">
          Invalid username or password.
        </div>
      )}
      {formState.kind === "account-disabled" && (
        <div className="alv-login__banner alv-login__banner--danger" role="alert">
          This account has been disabled. Contact an administrator.
        </div>
      )}
      {formState.kind === "locked-out" && (
        <div className="alv-login__banner alv-login__banner--danger" role="alert">
          This account is temporarily locked after too many failed attempts.
          {formState.lockedUntilUtc && ` Try again after ${new Date(formState.lockedUntilUtc).toLocaleTimeString()}.`}
        </div>
      )}
      {formState.kind === "rate-limited" && (
        <div className="alv-login__banner alv-login__banner--danger" role="alert">
          Too many attempts from this device. Please wait a moment and try again.
        </div>
      )}
      {formState.kind === "unexpected-error" && (
        <div className="alv-login__banner alv-login__banner--danger" role="alert">
          Something went wrong. Please try again.
        </div>
      )}

      <form className="alv-login__form" onSubmit={handleSubmit}>
        <FormField
          label="Username"
          name="username"
          autoComplete="username"
          required
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          disabled={submitting}
        />
        <FormField
          label="Password"
          name="password"
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          disabled={submitting}
        />
        <Button type="submit" variant="primary" disabled={submitting}>
          {submitting ? "Signing in…" : "Sign in"}
        </Button>
      </form>
    </div>
  );
}
