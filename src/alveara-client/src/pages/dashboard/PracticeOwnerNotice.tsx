import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { useAuth } from "../../contexts/AuthContext";
import { ApiError } from "../../services/authApi";
import { designatePracticeOwner } from "../../services/accountsApi";
import { listStaff } from "../../services/configApi";
import { listUsers } from "../../services/authApi";
import type { StaffRecord } from "../../services/configApi";
import "./PracticeOwnerNotice.css";

/** The Deployment Administrator's notice while no Practice Owner exists (brief section 9; wording approved by the owner 2026-11-24). */
export const DEPLOYMENT_ADMINISTRATOR_NOTICE =
  "No Practice Owner has been created yet. This deployment account is intended to create the first Practice Owner. Before configuring the practice, " +
  "add the owner as a staff member and select Practice Owner. The Practice Owner will use the guided practice setup when it becomes available. " +
  "You can still configure the practice manually with this account if necessary.";

/**
 * ALV-N030 (brief sections 4 and 9; D-3, D-7). While no Practice Owner exists:
 * <list>
 * - the Deployment Administrator sees the recommendation to add the owner as a staff member (the "Add staff member" form has the Practice Owner box);
 * - any other administrator (an upgraded installation) sees the transition notice and can designate an existing active staff member with a login,
 *   after a confirmation step. The server accepts only the first designation ever made (409 afterwards).
 * </list>
 * Nothing shows once an owner exists, or for anyone without Manage users.
 */
/** ALV-N030-C01 (brief 18.7): what each failing owner condition means, in the notice's words. */
const OWNER_PROBLEMS: Record<string, string> = {
  staffInactive: "their staff profile is inactive",
  noLogin: "their staff profile has no login",
  loginDisabled: "their login is disabled",
  notAdministrator: "their login is not an Administrator",
  notStaffAccount: "their login is not a staff account",
};

export function PracticeOwnerNotice() {
  const { state, hasPermission, refresh } = useAuth();
  if (state.kind === "signed-in" && state.practiceOwnerStatus === "degraded" && hasPermission("ManageUsers")) {
    const problems = (state.practiceOwnerProblems ?? []).map((p) => OWNER_PROBLEMS[p] ?? p);
    return (
      <section className="alv-dashboard-notice" role="region" aria-label="Practice Owner">
        <p>
          The Practice Owner does not meet the owner requirements{problems.length > 0 ? `: ${problems.join("; ")}` : ""}. The Practice Owner must be an active
          staff member whose login is an enabled Administrator. To fix it, change their role to Administrator or re-enable their login in Security Administration,
          or have the Deployment Administrator transfer the Practice Owner designation to someone who meets the requirements.
        </p>
        <Link to="/admin/users">Open Security Administration</Link>
      </section>
    );
  }
  if (state.kind !== "signed-in" || state.practiceOwnerExists !== false || !hasPermission("ManageUsers")) return null;
  if (state.accountKind === "DeploymentAdministrator") {
    return (
      <section className="alv-dashboard-notice" role="region" aria-label="Practice Owner">
        <p>{DEPLOYMENT_ADMINISTRATOR_NOTICE}</p>
        <Link to="/admin/configuration">Open Practice Configuration (Staff, Add staff member)</Link>
      </section>
    );
  }
  return <DesignateOwner onDone={refresh} />;
}

function DesignateOwner({ onDone }: { onDone: () => Promise<void> }) {
  const [open, setOpen] = useState(false);
  const [staff, setStaff] = useState<StaffRecord[] | null>(null);
  // ALV-N030-C01 (brief 18.8, E1): only a staff member whose login is an enabled Administrator can be designated; others are listed but disabled
  const [admins, setAdmins] = useState<Set<string>>(new Set());
  const [loadError, setLoadError] = useState(false);
  const [chosen, setChosen] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open || staff !== null) return;
    let cancelled = false;
    Promise.all([listStaff(false), listUsers()])
      .then(([rows, users]) => {
        if (cancelled) return;
        setAdmins(new Set(users.filter((u) => u.role === "Admin" && !u.isDisabled).map((u) => u.id)));
        setStaff(rows.filter((r) => r.isActive && r.userAccountId));
      })
      .catch((err) => { console.warn("Loading the staff list failed", err); if (!cancelled) setLoadError(true); });
    return () => { cancelled = true; };
  }, [open, staff]);

  async function designate() {
    if (!chosen || !confirmed) return setError("Choose the Practice Owner and confirm.");
    setBusy(true);
    setError(null);
    try {
      await designatePracticeOwner(chosen);
      await onDone();
    } catch (err) {
      const code = err instanceof ApiError ? err.code : "";
      setError(code === "owner_already_designated" ? "A Practice Owner has already been designated." :
        code === "owner_target_invalid" ? "The Practice Owner must be an active staff member with an enabled login." :
        code === "owner_must_be_administrator" ? "Change this person's role to Administrator in Security Administration first." :
        "The Practice Owner could not be designated. Please try again.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="alv-dashboard-notice" role="region" aria-label="Practice Owner">
      <p>This practice has no Practice Owner yet. Designate the staff member who owns the practice; only one person can be the Practice Owner, and the guided practice setup is reserved for them.</p>
      {!open && <Button variant="primary" onClick={() => setOpen(true)}>Designate the Practice Owner</Button>}
      {open && (
        <div role="group" aria-label="Designate the Practice Owner">
          {loadError && <p role="alert">The staff list could not be loaded.</p>}
          {staff && staff.length === 0 && <p>No active staff member has a login yet. Add the owner in Practice Configuration, Staff, first.</p>}
          {staff && staff.length > 0 && (
            <>
              <label>
                Practice Owner
                <select value={chosen} onChange={(e) => setChosen(e.target.value)} disabled={busy}>
                  <option value="">Select…</option>
                  {staff.map((s) => {
                    const eligible = s.userAccountId !== null && admins.has(s.userAccountId);
                    return (
                      <option key={s.id} value={s.id} disabled={!eligible}>
                        {s.displayName}{s.jobTitle ? ` (${s.jobTitle})` : ""}{eligible ? "" : " - change this person's role to Administrator first"}
                      </option>
                    );
                  })}
                </select>
              </label>
              <p className="alv-form-field__hint">The Practice Owner signs in with the Administrator role. Change a person's role to Administrator in Security Administration first.</p>
              <label>
                <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} disabled={busy} /> I confirm this person is the Practice Owner. This cannot be changed here later.
              </label>
            </>
          )}
          {error && <p role="alert">{error}</p>}
          <Button onClick={() => setOpen(false)} disabled={busy}>Cancel</Button>
          {staff && staff.length > 0 && <Button variant="primary" onClick={() => void designate()} disabled={busy || !chosen || !confirmed}>Designate</Button>}
        </div>
      )}
    </section>
  );
}
