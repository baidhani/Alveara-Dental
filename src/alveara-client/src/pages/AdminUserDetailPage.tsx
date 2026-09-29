import { useCallback, useEffect, useState } from "react";
import type { FormEvent } from "react";
import { useLocation, useParams } from "react-router-dom";
import { PageHeader } from "../components/PageHeader";
import { LoadingState, ErrorState } from "../components/StatePatterns";
import { PermissionDenied } from "../components/PermissionDenied";
import { FormField } from "../components/FormField";
import { Button } from "../components/Button";
import { useNotifications } from "../components/Notification";
import { useAuth } from "../contexts/AuthContext";
import {
  ApiError,
  changeRole,
  issuePasswordReset,
  listUsers,
  revokeSessions,
  setEnabled,
  setSessionTimeout,
} from "../services/authApi";
import type { AdminUserSummary } from "../services/authApi";
import "./AdminUsersPage.css";

const ROLES = ["Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager", "Admin"];

type PageState =
  | { kind: "loading" }
  | { kind: "permission-denied" }
  | { kind: "error" }
  | { kind: "not-found" }
  | { kind: "loaded"; user: AdminUserSummary };

interface LocationState {
  user?: AdminUserSummary;
}

/**
 * ALV-001-C01's user-detail workflow (review finding ALV-001-C01-R01-05): a single account's
 * full state, plus the same role/enable/reset/revoke actions AdminUsersPage offers, plus the
 * authorized session-timeout configuration control the story requires and R01 was missing
 * entirely. There is no single-user GET endpoint, so this refetches the list and finds the
 * matching row when reached directly (no location.state) - not the most efficient shape, but a
 * real, honest fetch rather than a client-only cache assumption.
 */
export function AdminUserDetailPage() {
  const { userId } = useParams<{ userId: string }>();
  const location = useLocation();
  const [state, setState] = useState<PageState>(() => {
    const stateUser = (location.state as LocationState | null)?.user;
    return stateUser ? { kind: "loaded", user: stateUser } : { kind: "loading" };
  });
  const [timeoutInput, setTimeoutInput] = useState("");
  const { notify } = useNotifications();
  const { hasPermission } = useAuth();

  const load = useCallback(async () => {
    setState({ kind: "loading" });
    try {
      const users = await listUsers();
      const user = users.find((u) => u.id === userId);
      setState(user ? { kind: "loaded", user } : { kind: "not-found" });
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        setState({ kind: "permission-denied" });
      } else {
        setState({ kind: "error" });
      }
    }
  }, [userId]);

  useEffect(() => {
    if (state.kind === "loading") load();
  }, [state.kind, load]);

  useEffect(() => {
    if (state.kind === "loaded") setTimeoutInput(String(state.user.sessionTimeoutMinutes));
  }, [state]);

  async function handleRoleChange(role: string) {
    if (!userId) return;
    try {
      await changeRole(userId, role);
      notify("success", "Role updated.");
      await load();
    } catch {
      notify("danger", "Could not update the role.");
    }
  }

  async function handleToggleEnabled(isDisabled: boolean) {
    if (!userId) return;
    try {
      await setEnabled(userId, isDisabled);
      notify("success", isDisabled ? "Account enabled." : "Account disabled.");
      await load();
    } catch {
      notify("danger", "Could not update the account's status.");
    }
  }

  async function handleIssueReset() {
    if (!userId) return;
    try {
      const { token } = await issuePasswordReset(userId);
      notify("info", `Reset code for the user: ${token}`);
    } catch {
      notify("danger", "Could not issue a reset code.");
    }
  }

  async function handleRevokeSessions() {
    if (!userId) return;
    try {
      await revokeSessions(userId);
      notify("success", "All sessions revoked for this account.");
    } catch {
      notify("danger", "Could not revoke sessions.");
    }
  }

  async function handleSetTimeout(event: FormEvent) {
    event.preventDefault();
    if (!userId) return;
    const minutes = Number(timeoutInput);
    try {
      await setSessionTimeout(userId, minutes);
      notify("success", "Session timeout updated.");
      await load();
    } catch (err) {
      notify("danger", err instanceof ApiError && err.code === "invalid_session_timeout" ? "Session timeout must be between 5 and 1440 minutes." : "Could not update the session timeout.");
    }
  }

  const canManageRoles = hasPermission("ManageRoles");
  const canManageStatus = hasPermission("ManageAccountStatus");
  const canIssueResets = hasPermission("IssuePasswordResets");
  const canRevokeSessions = hasPermission("RevokeSessions");

  return (
    <>
      <PageHeader title="User details" />

      {state.kind === "loading" && <LoadingState label="Loading user…" />}
      {state.kind === "permission-denied" && <PermissionDenied requiredPermission="ManageUsers" />}
      {state.kind === "error" && <ErrorState title="Could not load this user" action={<Button onClick={load}>Retry</Button>} />}
      {state.kind === "not-found" && <ErrorState title="User not found" description="This account may have been removed." />}

      {state.kind === "loaded" && (
        <div className="alv-admin-users">
          <dl>
            <dt>Username</dt>
            <dd>{state.user.username}</dd>
            <dt>Role</dt>
            <dd>
              {canManageRoles ? (
                <select aria-label="Role" value={state.user.role} onChange={(e) => handleRoleChange(e.target.value)}>
                  {!ROLES.includes(state.user.role) && <option value={state.user.role}>{state.user.role}</option>}
                  {ROLES.map((role) => (
                    <option key={role} value={role}>
                      {role}
                    </option>
                  ))}
                </select>
              ) : (
                state.user.role
              )}
            </dd>
            <dt>Status</dt>
            <dd>
              <span className={`alv-status-badge${state.user.isDisabled ? " alv-status-badge--disabled" : " alv-status-badge--enabled"}`}>
                {state.user.isDisabled ? "Disabled" : "Enabled"}
              </span>
            </dd>
            <dt>MFA</dt>
            <dd>{state.user.mfaEnabled ? "Enabled" : "Not enabled"}</dd>
            <dt>Created</dt>
            <dd>{new Date(state.user.createdAtUtc).toLocaleString()}</dd>
          </dl>

          <div className="alv-admin-users__actions">
            {canManageStatus && (
              <Button variant="secondary" onClick={() => handleToggleEnabled(state.user.isDisabled)}>
                {state.user.isDisabled ? "Enable" : "Disable"}
              </Button>
            )}
            {canIssueResets && (
              <Button variant="secondary" onClick={handleIssueReset}>
                Issue password reset
              </Button>
            )}
            {canRevokeSessions && (
              <Button variant="danger" onClick={handleRevokeSessions}>
                Revoke sessions
              </Button>
            )}
          </div>

          {canManageStatus && (
            <form onSubmit={handleSetTimeout} style={{ marginTop: "1.5rem", maxWidth: 320 }}>
              <FormField
                label="Session timeout (minutes)"
                name="sessionTimeoutMinutes"
                type="number"
                min={5}
                max={1440}
                value={timeoutInput}
                onChange={(e) => setTimeoutInput(e.target.value)}
                hint="Between 5 and 1440 minutes (24 hours)."
              />
              <Button type="submit" variant="secondary">
                Update timeout
              </Button>
            </form>
          )}
        </div>
      )}
    </>
  );
}
