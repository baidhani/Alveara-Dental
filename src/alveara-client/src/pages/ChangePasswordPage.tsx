import { useState } from "react";
import type { FormEvent } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { PageHeader } from "../components/PageHeader";
import { FormField } from "../components/FormField";
import { Button } from "../components/Button";
import { LoadingState } from "../components/StatePatterns";
import { useAuth } from "../contexts/AuthContext";
import { ApiError } from "../services/authApi";
import { changeOwnPassword } from "../services/accountsApi";
import "./LoginPage.css";

export const MINIMUM_PASSWORD_LENGTH = 12;

const ERRORS: Record<string, string> = {
  invalid_current_password: "The current password is not correct.",
  password_too_short: `Choose a password of at least ${MINIMUM_PASSWORD_LENGTH} characters.`,
  password_unchanged: "Choose a password that is different from the current one.",
};

/**
 * ALV-N030 (brief section 9): "Choose a new password". Shown instead of the app while the account has a temporary password (the route guard sends
 * it here; the server refuses everything else until the change is made). Also usable by anyone signed in who wants to change their password.
 * On success the server re-issues the session and the app opens normally.
 */
export function ChangePasswordPage() {
  const { state, refresh, logout } = useAuth();
  const navigate = useNavigate();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (state.kind === "loading") return <LoadingState label="Checking your session…" />;
  if (state.kind === "signed-out") return <Navigate to="/login" replace />;
  const forced = state.mustChangePassword === true;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (next.length < MINIMUM_PASSWORD_LENGTH) return setError(ERRORS.password_too_short);
    if (next !== confirm) return setError("The two new passwords do not match.");
    setSubmitting(true);
    try {
      await changeOwnPassword(current, next);
      await refresh();
      navigate("/", { replace: true });
    } catch (err) {
      setError(err instanceof ApiError ? ERRORS[err.code] ?? "Your password could not be changed. Please try again." : "Your password could not be changed. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="alv-login">
      <PageHeader
        title="Choose a new password"
        description={forced
          ? "You signed in with a temporary password. Choose your own password to continue."
          : "Enter your current password and choose a new one."}
      />
      {error && (
        <div className="alv-login__banner alv-login__banner--danger" role="alert">
          {error}
        </div>
      )}
      <form className="alv-login__form" onSubmit={handleSubmit} noValidate>
        <FormField label={forced ? "Temporary password" : "Current password"} name="currentPassword" type="password" autoComplete="current-password"
          required value={current} onChange={(e) => setCurrent(e.target.value)} disabled={submitting} />
        <FormField label="New password" name="newPassword" type="password" autoComplete="new-password" required
          hint={`At least ${MINIMUM_PASSWORD_LENGTH} characters.`} value={next} onChange={(e) => setNext(e.target.value)} disabled={submitting} />
        <FormField label="Confirm the new password" name="confirmPassword" type="password" autoComplete="new-password" required
          value={confirm} onChange={(e) => setConfirm(e.target.value)} disabled={submitting} />
        <Button type="submit" variant="primary" disabled={submitting}>
          {submitting ? "Saving…" : "Save my new password"}
        </Button>
        <Button type="button" onClick={() => void logout()} disabled={submitting}>
          Sign out
        </Button>
      </form>
    </div>
  );
}
