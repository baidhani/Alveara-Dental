import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
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
import { getPracticeOwner } from "../services/accountsApi";
import { TransferPracticeOwnerDialog } from "./TransferPracticeOwnerDialog";
import { OWNER_PROTECTED } from "./configuration/PeopleTabs";
import { accountLabels } from "./accountLabels";
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
  const { hasPermission, state: auth, refresh: refreshIdentity } = useAuth();
  // ALV-N030-C01 (brief 18.8): only the Deployment Administrator transfers the Practice Owner designation; the server checks the account kind regardless
  const isDeploymentAdministrator = auth.kind === "signed-in" && auth.accountKind === "DeploymentAdministrator";
  const [transferOpen, setTransferOpen] = useState(false);
  const [ownerKey, setOwnerKey] = useState(0);
  // ALV-N030: the Practice Owner's staff name, for the label (only a Manage users holder may read it; others see no owner label)
  const [ownerName, setOwnerName] = useState<string | null>(null);
  const canReadOwner = hasPermission("ManageUsers");
  useEffect(() => {
    if (!canReadOwner) return;
    let cancelled = false;
    getPracticeOwner()
      .then((r) => { if (!cancelled) setOwnerName(r.owner?.displayName ?? null); })
      .catch((err) => console.warn("Reading the Practice Owner failed; the label is not shown", err));
    return () => { cancelled = true; };
  }, [canReadOwner, ownerKey]);
  /** ALV-N030-C01 (E3, E5): the owner's role and enabled state are locked until the designation is transferred (the server refuses regardless). */
  const isOwner = (user: AdminUserSummary) => ownerName !== null && user.linkedStaffDisplayName === ownerName;

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
      <PageHeader title="Security administration" description={hasPermission("ManageUsers") ? "Manage user accounts, roles, and sessions." : "The accounts your role may act on: you can see only accounts whose role holds no permission that yours lacks, never an administrator."} />

      {isDeploymentAdministrator && ownerName && state.kind === "loaded" && !transferOpen && (
        <Button onClick={() => setTransferOpen(true)}>Transfer Practice Owner</Button>
      )}
      {transferOpen && state.kind === "loaded" && (
        <TransferPracticeOwnerDialog
          users={state.users}
          mfaEnabled={state.users.find((u) => u.username === (auth.kind === "signed-in" ? auth.username : ""))?.mfaEnabled === true}
          onCancel={() => setTransferOpen(false)}
          onDone={async (name) => {
            setTransferOpen(false);
            notify("success", `${name} is now the Practice Owner.`);
            setOwnerKey((k) => k + 1);
            await refreshIdentity();
            await load();
          }}
        />
      )}
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
              <th>Staff profile</th>
              <th>Details</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {state.users.map((user) => (
              <tr key={user.id}>
                <td>
                  {user.username}
                  {accountLabels(user, ownerName).map((label) => (
                    <span key={label} className="alv-account-label">{label}</span>
                  ))}
                </td>
                <td>
                  {canManageRoles ? (
                    <select
                      aria-label={`Role for ${user.username}`}
                      value={user.role}
                      disabled={isOwner(user) && user.role === "Admin"}
                      title={isOwner(user) && user.role === "Admin" ? "Practice Owner: must stay Administrator. Transfer the Practice Owner designation first." : undefined}
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
                <td>{user.linkedStaffDisplayName ? `${user.linkedStaffDisplayName}${user.linkedStaffIsActive === false ? " (inactive)" : ""}` : "None"}</td>
                <td>
                  <Link to={`/admin/users/${user.id}`} state={{ user }}>
                    View
                  </Link>
                </td>
                <td className="alv-admin-users__actions">
                  {canManageStatus && (
                    <Button variant="secondary" onClick={() => handleToggleEnabled(user)} disabled={isOwner(user) && !user.isDisabled}
                      title={isOwner(user) && !user.isDisabled ? OWNER_PROTECTED : undefined}>
                      {user.isDisabled ? "Enable" : "Disable"}
                    </Button>
                  )}
                  {isOwner(user) && <span className="alv-form-field__hint">{OWNER_PROTECTED}</span>}
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
