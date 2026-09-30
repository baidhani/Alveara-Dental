import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { PageHeader } from "../components/PageHeader";
import { FormField } from "../components/FormField";
import { Button } from "../components/Button";
import { ApiError, completeMfaChallenge } from "../services/authApi";
import { useAuth } from "../contexts/AuthContext";
import "./LoginPage.css";

interface LocationState {
  challengeToken?: string;
  /** ALV-N009 R02: the deep-link destination LoginPage was carrying before MFA interrupted it,
   *  so a successful challenge returns the caller there instead of always to "/". */
  redirectTo?: string;
}

/**
 * ALV-001-C01's MFA challenge + recovery screen: the same code field accepts either a live TOTP
 * code from an authenticator app or a one-time recovery code - both are verified entirely on the
 * server with no network call to any third party, so this screen works with no public internet
 * available, exactly like the underlying MFA mechanism itself.
 */
export function MfaChallengePage() {
  const location = useLocation();
  const navigate = useNavigate();
  const { refresh } = useAuth();
  const state = location.state as LocationState | null;
  const challengeToken = state?.challengeToken;
  const redirectTo = state?.redirectTo ?? "/";

  const [code, setCode] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!challengeToken) {
      // Reached directly (e.g. a stale bookmark or reload) rather than via a real login - there
      // is no challenge in flight to complete, so send the user back to start over honestly
      // instead of showing a form that can never succeed.
      navigate("/login", { replace: true });
    }
  }, [challengeToken, navigate]);

  if (!challengeToken) {
    return null;
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      await completeMfaChallenge(challengeToken!, code);
      await refresh();
      navigate(redirectTo, { replace: true });
    } catch (err) {
      if (err instanceof ApiError && (err.status === 401 || err.status === 429)) {
        setError(
          err.status === 429
            ? "Too many attempts from this device. Please wait a moment and try again."
            : "Invalid or expired code. Enter a current authenticator code or an unused recovery code."
        );
      } else {
        setError("Something went wrong. Please try again.");
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="alv-login">
      <PageHeader
        title="Enter your authentication code"
        description="Enter the current code from your authenticator app, or a recovery code if the app is unavailable."
      />

      {error && (
        <div className="alv-login__banner alv-login__banner--danger" role="alert">
          {error}
        </div>
      )}

      <form className="alv-login__form" onSubmit={handleSubmit}>
        <FormField
          label="Authentication or recovery code"
          name="code"
          autoComplete="one-time-code"
          required
          value={code}
          onChange={(e) => setCode(e.target.value)}
          disabled={submitting}
        />
        <Button type="submit" variant="primary" disabled={submitting}>
          {submitting ? "Verifying…" : "Verify"}
        </Button>
      </form>
    </div>
  );
}
