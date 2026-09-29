import { useCallback, useEffect, useState } from "react";
import { PageHeader } from "../components/PageHeader";
import { LoadingState, ErrorState, EmptyState } from "../components/StatePatterns";
import { PermissionDenied } from "../components/PermissionDenied";
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
} from "../services/authApi";
import type { AdminUserSummary } from "../services/authApi";
import "./AdminUsersPage.css";

const ROLES = ["Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager", "Admin"];

type PageState =
  | { kind: "loading" }
  | { kind: "permission-denied" }
  | { kind: "error" }
  | { kind: "loaded"; users: AdminUserSummary[] };

/**
 * ALV-001-C01's security-administration UI: user list with status/role/MFA state, and the
 * role-change, enable/disable, password-reset, and session-revocation actions from
 * AuthController's admin endpoints. Every action re-fetches the list afterward rather than
 * optimistically patching local state, so what's on screen always reflects a real server
 * response, not an assumption about what the write did.
 */
export function AdminUsersPage() {
  const [state, setState] = useState<PageState>({ kind: "loading" });
  const { notify } = useNotifications();
  const { hasPermission } = useAuth();

  const load = useCallback(async () => {
    setState({ kind: "loading" });
    try {
      const users = await listUsers();
      setState({ kind: "loaded", users });
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        setState({ kind: "permission-denied" });
      } else {
        setState({ kind: "error" });
      }
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function handleRoleChange(userId: string, role: string) {
    try {
      await changeRole(userId, role);
      notify("success", "Role updated.");
      await load();
    } catch {
      notify("danger", "Could not update the role.");
    }
  }

  async function handleToggleEnabled(user: AdminUserSummary) {
    try {
      await setEnabled(user.id, user.isDisabled);
      notify("success", user.isDisabled ? "Account enabled." : "Account disabled.");
      await load();
    } catch {
      notify("danger", "Could not update the account's status.");
    }
  }

  async function handleIssueReset(userId: string) {
    try {
      const { token } = await issuePasswordReset(userId);
      // Shown exactly once, in-page — the API never stores or logs this value, so this is the
      // only place it is ever visible again.
      notify("info", `Reset code for the user: ${token}`);
    } catch {
      notify("danger", "Could not issue a reset code.");
    }
  }

  async function handleRevokeSessions(userId: string) {
    try {
      await revokeSessions(userId);
      notify("success", "All sessions revoked for this account.");
    } catch {
      notify("danger", "Could not revoke sessions.");
    }
  }

  const canManageRoles = hasPermission("ManageRoles");
  const canManageStatus = hasPermission("ManageAccountStatus");
  const canIssueResets = hasPermission("IssuePasswordResets");
  const canRevokeSessions = hasPermission("RevokeSessions");

  return (
    <>
      <PageHeader title="Security administration" description="Manage user accounts, roles, and sessions." />

      {state.kind === "loading" && <LoadingState label="Loading users…" />}
      {state.kind === "permission-denied" && <PermissionDenied requiredPermission="ManageUsers" />}
      {state.kind === "error" && (
        <ErrorState title="Could not load users" action={<Button onClick={load}>Retry</Button>} />
      )}
      {state.kind === "loaded" && state.users.length === 0 && (
        <EmptyState title="No users yet" description="No accounts have been created." />
      )}

      {state.kind === "loaded" && state.users.length > 0 && (
        <table className="alv-admin-users">
          <thead>
            <tr>
              <th>Username</th>
              <th>Role</th>
              <th>Status</th>
              <th>MFA</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {state.users.map((user) => (
              <tr key={user.id}>
                <td>{user.username}</td>
                <td>
                  {canManageRoles ? (
                    <select
                      aria-label={`Role for ${user.username}`}
                      value={user.role}
                      onChange={(e) => handleRoleChange(user.id, e.target.value)}
                    >
                      {!ROLES.includes(user.role) && <option value={user.role}>{user.role}</option>}
                      {ROLES.map((role) => (
                        <option key={role} value={role}>
                          {role}
                        </option>
                      ))}
                    </select>
                  ) : (
                    user.role
                  )}
                </td>
                <td>
                  <span className={`alv-status-badge${user.isDisabled ? " alv-status-badge--disabled" : " alv-status-badge--enabled"}`}>
                    {user.isDisabled ? "Disabled" : "Enabled"}
                  </span>
                </td>
                <td>{user.mfaEnabled ? "Enabled" : "Not enabled"}</td>
                <td className="alv-admin-users__actions">
                  {canManageStatus && (
                    <Button variant="secondary" onClick={() => handleToggleEnabled(user)}>
                      {user.isDisabled ? "Enable" : "Disable"}
                    </Button>
                  )}
                  {canIssueResets && (
                    <Button variant="secondary" onClick={() => handleIssueReset(user.id)}>
                      Issue password reset
                    </Button>
                  )}
                  {canRevokeSessions && (
                    <Button variant="danger" onClick={() => handleRevokeSessions(user.id)}>
                      Revoke sessions
                    </Button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  );
}
