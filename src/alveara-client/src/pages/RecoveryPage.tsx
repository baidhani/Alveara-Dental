import { useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import { PageHeader } from "../components/PageHeader";
import { FormField } from "../components/FormField";
import { Button } from "../components/Button";
import { ApiError } from "../services/authApi";
import { recoverDeploymentAdministrator } from "../services/accountsApi";
import { MINIMUM_PASSWORD_LENGTH } from "./ChangePasswordPage";
import "./LoginPage.css";

/**
 * ALV-N030-C01 (brief 18.6, 18.8): Deployment Administrator recovery, unauthenticated, linked from nowhere in the app. The installer's command
 * `recovery issue-token` prints a one-time token on the server's console; it is typed here (a form field only, never the URL) with the Deployment Administrator's
 * username - or a new username when the installation has none - and a new password. Every token failure, and a username that is not the Deployment
 * Administrator's, has the same message. Recovery turns the account's MFA off; it can be set up again under Multi-Factor Authentication after signing in.
 */
export function RecoveryPage() {
  const [token, setToken] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<{ username: string; method: string } | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (token.trim() === "" || username.trim() === "") return setError("Enter the recovery token and the username.");
    if (password.length < MINIMUM_PASSWORD_LENGTH) return setError(`Choose a password of at least ${MINIMUM_PASSWORD_LENGTH} characters.`);
    if (password !== confirm) return setError("The two passwords do not match.");
    setSubmitting(true);
    try {
      setDone(await recoverDeploymentAdministrator(token.trim(), username.trim(), password));
      setToken("");
      setPassword("");
      setConfirm("");
    } catch (err) {
      if (err instanceof ApiError && err.code === "recovery_failed")
        setError("Recovery failed. The token may be wrong, expired or already used, or the username is not the Deployment Administrator's. Ask for a new token from the installer.");
      else if (err instanceof ApiError && err.code === "password_too_short") setError(`Choose a password of at least ${MINIMUM_PASSWORD_LENGTH} characters.`);
      else if (err instanceof ApiError && err.code === "username_taken") setError("That username is already in use by a staff account. Choose another.");
      else if (err instanceof ApiError && err.status === 429) setError("Too many attempts. Wait a few minutes, then try again.");
      else setError("Recovery could not be completed. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  if (done) {
    return (
      <div className="alv-login">
        <PageHeader title="Deployment Administrator recovery" description="Recovery is complete." />
        <p role="status">
          {done.method === "Created"
            ? `The Deployment Administrator "${done.username}" was created. Sign in with the new password.`
            : `The Deployment Administrator "${done.username}" was reset: the new password is set, the account is enabled, and two-factor authentication is off. Sign in, then set up two-factor authentication again under Multi-Factor Authentication.`}
        </p>
        <Link to="/login">Go to sign in</Link>
      </div>
    );
  }

  return (
    <div className="alv-login">
      <PageHeader title="Deployment Administrator recovery"
        description="Enter the recovery token the installer showed on the server, the Deployment Administrator's username (or a new one if this installation has none), and a new password." />
      {error && <div className="alv-login__banner alv-login__banner--danger" role="alert">{error}</div>}
      <form className="alv-login__form" onSubmit={handleSubmit} noValidate aria-label="Deployment Administrator recovery">
        <FormField label="Recovery token" name="recoveryToken" autoComplete="off" spellCheck={false} required value={token}
          onChange={(e) => setToken(e.target.value)} disabled={submitting} />
        <FormField label="Username" name="username" autoComplete="username" required value={username}
          onChange={(e) => setUsername(e.target.value)} disabled={submitting} />
        <FormField label="New password" name="newPassword" type="password" autoComplete="new-password" required
          hint={`At least ${MINIMUM_PASSWORD_LENGTH} characters.`} value={password} onChange={(e) => setPassword(e.target.value)} disabled={submitting} />
        <FormField label="Confirm the new password" name="confirmPassword" type="password" autoComplete="new-password" required value={confirm}
          onChange={(e) => setConfirm(e.target.value)} disabled={submitting} />
        <Button type="submit" variant="primary" disabled={submitting}>{submitting ? "Recovering…" : "Recover the Deployment Administrator"}</Button>
      </form>
    </div>
  );
}
