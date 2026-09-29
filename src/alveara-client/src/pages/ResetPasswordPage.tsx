import { useState } from "react";
import type { FormEvent } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { PageHeader } from "../components/PageHeader";
import { FormField } from "../components/FormField";
import { Button } from "../components/Button";
import { ApiError, completePasswordReset } from "../services/authApi";
import "./LoginPage.css";

/**
 * ALV-001-C01's password recovery screen. Completes an admin-issued, one-time reset token (no
 * email/SMS dependency - the token is handed to the user out-of-band by an administrator, e.g.
 * read aloud or written down, matching the no-public-internet requirement the story sets for
 * every recovery path). Reads userId/token from the URL so an admin can hand the user a single
 * link, but both fields stay editable for a token relayed by voice instead.
 */
export function ResetPasswordPage() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const [userId, setUserId] = useState(searchParams.get("userId") ?? "");
  const [token, setToken] = useState(searchParams.get("token") ?? "");
  const [newPassword, setNewPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      await completePasswordReset(userId, token, newPassword);
      setDone(true);
    } catch (err) {
      setError(
        err instanceof ApiError && err.code === "invalid_or_expired_token"
          ? "This reset link is invalid, expired, or has already been used. Ask an administrator to issue a new one."
          : "Something went wrong. Please try again."
      );
    } finally {
      setSubmitting(false);
    }
  }

  if (done) {
    return (
      <div className="alv-login">
        <PageHeader title="Password reset" description="Your password has been reset." />
        <Button variant="primary" onClick={() => navigate("/login")}>
          Continue to sign in
        </Button>
      </div>
    );
  }

  return (
    <div className="alv-login">
      <PageHeader
        title="Reset your password"
        description="Enter the reset code your administrator gave you and choose a new password."
      />

      {error && (
        <div className="alv-login__banner alv-login__banner--danger" role="alert">
          {error}
        </div>
      )}

      <form className="alv-login__form" onSubmit={handleSubmit}>
        <FormField
          label="User ID"
          name="userId"
          required
          value={userId}
          onChange={(e) => setUserId(e.target.value)}
          disabled={submitting}
        />
        <FormField
          label="Reset code"
          name="token"
          required
          value={token}
          onChange={(e) => setToken(e.target.value)}
          disabled={submitting}
        />
        <FormField
          label="New password"
          name="newPassword"
          type="password"
          autoComplete="new-password"
          required
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          disabled={submitting}
        />
        <Button type="submit" variant="primary" disabled={submitting}>
          {submitting ? "Resetting…" : "Reset password"}
        </Button>
      </form>
    </div>
  );
}
