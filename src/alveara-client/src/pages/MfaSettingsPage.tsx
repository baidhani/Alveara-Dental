import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Navigate } from "react-router-dom";
import { PageHeader } from "../components/PageHeader";
import { FormField } from "../components/FormField";
import { Button } from "../components/Button";
import { LoadingState, ErrorState } from "../components/StatePatterns";
import { useAuth } from "../contexts/AuthContext";
import { ApiError, confirmMfa, enrollMfa, getMyPermissions } from "../services/authApi";
import type { MfaEnrollmentResult } from "../services/authApi";
import "./LoginPage.css";
import "./MfaSettingsPage.css";

type PageState =
  | { kind: "loading" }
  | { kind: "signed-out" } // not an error - just needs to sign in first
  | { kind: "error" }
  | { kind: "idle" } // no enrollment in progress
  | { kind: "step-up-required" } // replacing an active factor: ask for current password first
  | { kind: "enrolling"; enrollment: MfaEnrollmentResult } // secret/codes shown, awaiting confirmation code
  | { kind: "confirmed" };

/**
 * ALV-001-C01's MFA enrollment/replacement screen (review finding ALV-001-C01-R01-05). Starting
 * enrollment never disables an already-active factor server-side (see AccountService's pending-
 * enrollment model) - this page reflects that: the secret/recovery codes shown here are inert
 * until the confirmation code below is submitted, and leaving this page without confirming costs
 * nothing.
 */
export function MfaSettingsPage() {
  const [state, setState] = useState<PageState>({ kind: "loading" });
  const [currentPassword, setCurrentPassword] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const { refresh } = useAuth();

  useEffect(() => {
    (async () => {
      try {
        const permissions = await getMyPermissions();
        if (permissions === null) {
          setState({ kind: "signed-out" });
          return;
        }
        // MFA-enabled state isn't in the permissions payload; deriving it from a fresh enroll
        // attempt would be wrong (that starts a replacement) - instead this page starts "idle"
        // and lets the user choose to begin enrollment, at which point the server tells us via
        // current_password_required whether a factor is already active.
        setState({ kind: "idle" });
      } catch {
        setState({ kind: "error" });
      }
    })();
  }, []);

  async function handleBeginEnrollment(event?: FormEvent, passwordOverride?: string) {
    event?.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      const enrollment = await enrollMfa(passwordOverride);
      setState({ kind: "enrolling", enrollment });
    } catch (err) {
      if (err instanceof ApiError && err.code === "current_password_required") {
        setState({ kind: "step-up-required" });
      } else if (err instanceof ApiError && err.code === "invalid_current_password") {
        setError("That password isn't correct.");
      } else {
        setError("Could not start MFA enrollment. Please try again.");
      }
    } finally {
      setSubmitting(false);
    }
  }

  async function handleConfirm(event: FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await confirmMfa(code);
      await refresh();
      setState({ kind: "confirmed" });
    } catch (err) {
      setError(
        err instanceof ApiError && err.code === "invalid_mfa_code"
          ? "That code isn't valid. Check the time on your authenticator app and try again."
          : "Could not confirm MFA. Please try again."
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="alv-login">
      <PageHeader
        title="Multi-factor authentication"
        description="Add or replace the authenticator app used to sign in. No network access is required to use it."
      />

      {state.kind === "loading" && <LoadingState label="Loading…" />}
      {state.kind === "signed-out" && <Navigate to="/login?reason=expired" replace />}
      {state.kind === "error" && <ErrorState title="Could not load MFA settings" />}

      {error && (
        <div className="alv-login__banner alv-login__banner--danger" role="alert">
          {error}
        </div>
      )}

      {state.kind === "idle" && (
        <Button variant="primary" onClick={() => handleBeginEnrollment()} disabled={submitting}>
          Set up an authenticator app
        </Button>
      )}

      {state.kind === "step-up-required" && (
        <>
          <p>An MFA factor is already active on this account. Enter your current password to replace it.</p>
          <form
            className="alv-login__form"
            onSubmit={(e) => {
              e.preventDefault();
              handleBeginEnrollment(undefined, currentPassword);
            }}
          >
            <FormField
              label="Current password"
              name="currentPassword"
              type="password"
              autoComplete="current-password"
              required
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              disabled={submitting}
            />
            <Button type="submit" variant="primary" disabled={submitting}>
              Continue
            </Button>
          </form>
        </>
      )}

      {state.kind === "enrolling" && (
        <div className="alv-mfa-enroll">
          <p>
            Scan this into your authenticator app, or enter the code manually. Your existing factor
            (if any) stays active until you confirm below.
          </p>
          <p className="alv-mfa-enroll__secret" data-testid="mfa-secret">
            {state.enrollment.base32Secret}
          </p>
          <p>Save these one-time recovery codes somewhere safe - each works once if you lose access to the app:</p>
          <ul className="alv-mfa-enroll__codes" data-testid="mfa-recovery-codes">
            {state.enrollment.recoveryCodes.map((rc) => (
              <li key={rc}>{rc}</li>
            ))}
          </ul>

          <form className="alv-login__form" onSubmit={handleConfirm}>
            <FormField
              label="Enter the current code from your authenticator app"
              name="code"
              autoComplete="one-time-code"
              required
              value={code}
              onChange={(e) => setCode(e.target.value)}
              disabled={submitting}
            />
            <Button type="submit" variant="primary" disabled={submitting}>
              {submitting ? "Confirming…" : "Confirm"}
            </Button>
          </form>
        </div>
      )}

      {state.kind === "confirmed" && (
        <div className="alv-login__banner" style={{ background: "var(--color-success-surface, rgba(22,163,74,0.12))", color: "var(--color-success, #15803d)" }}>
          MFA is now active on your account.
        </div>
      )}
    </div>
  );
}
