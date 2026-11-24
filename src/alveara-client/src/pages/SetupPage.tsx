import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { PageHeader } from "../components/PageHeader";
import { FormField } from "../components/FormField";
import { Button } from "../components/Button";
import { LoadingState } from "../components/StatePatterns";
import { ApiError } from "../services/authApi";
import { completeSetup, getSetupStatus } from "../services/accountsApi";
import { MINIMUM_PASSWORD_LENGTH } from "./ChangePasswordPage";
import "./LoginPage.css";

/**
 * ALV-N030 (brief sections 4 and 9): first-run setup, unauthenticated. The installer prints a one-time setup token; it is typed into this form
 * (a form field only - never read from or written to the URL) together with the Deployment Administrator's username and password. Every token
 * failure has the same message, whatever the cause. Once setup is complete the page says so and links to sign-in.
 */
/** ALV-N030 (brief 16.2b): after setup the server restarts; the page checks the status every 2 s until it is back in normal mode, and gives up after 120 s. */
export const RESTART_POLL_MS = 2000;
export const RESTART_GIVE_UP_MS = 120_000;

export function SetupPage({ pollMs = RESTART_POLL_MS, giveUpMs = RESTART_GIVE_UP_MS }: { pollMs?: number; giveUpMs?: number } = {}) {
  const navigate = useNavigate();
  const [status, setStatus] = useState<"loading" | "available" | "completed" | "error" | "restarting" | "did-not-return">("loading");
  const [token, setToken] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    getSetupStatus()
      .then((s) => { if (!cancelled) setStatus(s.setupAvailable ? "available" : "completed"); })
      .catch((err) => { console.warn("Reading the setup status failed", err); if (!cancelled) setStatus("error"); });
    return () => { cancelled = true; };
  }, []);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (token.trim() === "" || username.trim() === "") return setError("Enter the setup token and a username.");
    if (password.length < MINIMUM_PASSWORD_LENGTH) return setError(`Choose a password of at least ${MINIMUM_PASSWORD_LENGTH} characters.`);
    if (password !== confirm) return setError("The two passwords do not match.");
    setSubmitting(true);
    try {
      await completeSetup(token.trim(), username.trim(), password);
      setStatus("restarting");
      await waitForRestart();
    } catch (err) {
      if (err instanceof ApiError && err.code === "setup_failed") setError("The setup token is not valid. It may be wrong, expired or already used; ask for a new one from the installer.");
      else if (err instanceof ApiError && err.code === "password_too_short") setError(`Choose a password of at least ${MINIMUM_PASSWORD_LENGTH} characters.`);
      else if (err instanceof ApiError && err.code === "username_taken") setError("That username is already in use.");
      else if (err instanceof ApiError && err.status === 429) setError("Too many attempts. Wait a few minutes, then try again.");
      else setError("Setup could not be completed. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  /** The server stops and starts again in normal mode; while it is down every check fails, which is expected. */
  async function waitForRestart() {
    const deadline = Date.now() + giveUpMs;
    while (Date.now() < deadline) {
      await new Promise((resolve) => setTimeout(resolve, pollMs));
      try {
        const s = await getSetupStatus();
        if (s && s.setupAvailable === false) {
          navigate("/login?setup=complete", { replace: true });
          return;
        }
      } catch (err) {
        console.debug("The server is not answering yet while it restarts", err);
      }
    }
    setStatus("did-not-return");
  }

  if (status === "loading") return <LoadingState label="Checking this installation…" />;

  if (status === "restarting" || status === "did-not-return") {
    return (
      <div className="alv-login">
        <PageHeader title="First-run setup" description="Setup is complete." />
        {status === "restarting"
          ? <p role="status">Setup is complete. DentalKeep is restarting…</p>
          : <p role="alert">DentalKeep did not come back. Check that the service is running.</p>}
      </div>
    );
  }

  if (status === "completed" || status === "error") {
    return (
      <div className="alv-login">
        <PageHeader
          title="First-run setup"
          description={status === "completed" ? "This installation has already been set up." : "The setup status could not be read. Check that the server is running."}
        />
        <Link to="/login">Go to sign in</Link>
      </div>
    );
  }

  return (
    <div className="alv-login">
      <PageHeader
        title="First-run setup"
        description="Create the Deployment Administrator, the account that sets up this installation. Enter the setup token the installer showed you."
      />
      {error && (
        <div className="alv-login__banner alv-login__banner--danger" role="alert">
          {error}
        </div>
      )}
      <form className="alv-login__form" onSubmit={handleSubmit} noValidate>
        <FormField label="Setup token" name="setupToken" autoComplete="off" spellCheck={false} required value={token}
          onChange={(e) => setToken(e.target.value)} disabled={submitting} />
        <FormField label="Username" name="username" autoComplete="username" required value={username}
          onChange={(e) => setUsername(e.target.value)} disabled={submitting} />
        <FormField label="Password" name="password" type="password" autoComplete="new-password" required
          hint={`At least ${MINIMUM_PASSWORD_LENGTH} characters.`} value={password} onChange={(e) => setPassword(e.target.value)} disabled={submitting} />
        <FormField label="Confirm the password" name="confirmPassword" type="password" autoComplete="new-password" required value={confirm}
          onChange={(e) => setConfirm(e.target.value)} disabled={submitting} />
        <Button type="submit" variant="primary" disabled={submitting}>
          {submitting ? "Setting up…" : "Create the Deployment Administrator"}
        </Button>
      </form>
    </div>
  );
}
