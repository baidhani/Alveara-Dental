import { useState } from "react";
import type { FormEvent } from "react";
import { Navigate, useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { PageHeader } from "../components/PageHeader";
import { FormField } from "../components/FormField";
import { Button } from "../components/Button";
import { login } from "../services/authApi";
import { classifyLoginFailure, destinationAfterSignIn } from "./loginOutcome";
import type { LoginFormState } from "./loginOutcome";
import { useAuth } from "../contexts/AuthContext";
import { useServerConnection } from "../contexts/connectionStatusContext";
import "./LoginPage.css";

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
  const { refresh, state: authState, connectionLost } = useAuth();
  const server = useServerConnection();

  // ALV-N009 / ALV-N020: RequireAuth records where an EXPIRED session was (and whose it was); a successful sign-in returns there only for that same user.
  // `state.from` is only ever a location object this app itself set, never attacker-controlled input.
  const navState = location.state as { from?: { pathname: string; search: string }; fromUser?: string } | null;
  const from = navState?.from;
  const fromUser = navState?.fromUser;

  // ALV-N020 (F-012): the expiry notice is never shown while the server cannot be reached (that is not an expiry).
  const sessionExpired = searchParams.get("reason") === "expired" && server !== "disconnected" && !connectionLost;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setFormState({ kind: "submitting" });

    try {
      const result = await login(username, password);
      if ("mfaRequired" in result) {
        // ALV-N009 R02: carry the recorded destination (and whose it was) through to the MFA challenge; the challenge page applies the same same-user rule.
        navigate("/mfa-challenge", { state: { challengeToken: result.challengeToken, from, fromUser } });
        return;
      }
      await refresh();
      navigate(destinationAfterSignIn(result.username, from, fromUser), { replace: true });
    } catch (err) {
      setFormState(classifyLoginFailure(err));
    }
  }

  const submitting = formState.kind === "submitting";

  // ALV-N020: a signed-in person who opens the sign-in page goes to the dashboard (not while this page is completing its own sign-in).
  if (authState.kind === "signed-in" && !submitting) return <Navigate to="/" replace />;

  return (
    <div className="alv-login">
      <PageHeader title="Sign in" description="Enter your username and password to access DentalKeep." />

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
      {formState.kind === "server-unavailable" && (
        <div className="alv-login__banner alv-login__banner--danger" role="alert">
          The server cannot be reached, so you cannot sign in right now. Try again when it is back.
        </div>
      )}
      {formState.kind === "server-problem" && (
        <div className="alv-login__banner alv-login__banner--danger" role="alert">
          The server had a problem and could not sign you in. Try again; if it keeps happening, contact your administrator.
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
