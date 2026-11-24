import { useEffect, useRef, useState } from "react";
import { Button } from "../../components/Button";
import { ApiError } from "../../services/authApi";
import { createLegacyLogin, WORKING_ROLES } from "../../services/accountsApi";
import type { StaffMemberCreated } from "../../services/accountsApi";
import type { StaffRecord } from "../../services/configApi";

/** What the temporary-password panel shows: who it is for, and the password itself (held only in this page's memory). */
export interface IssuedLogin {
  displayName: string;
  username: string;
  temporaryPassword: string;
}

/**
 * ALV-N030 (brief section 9): the temporary password is shown ONCE, in the page, never stored in the browser. "Copy" puts it on the clipboard;
 * "Done" hides it for good. The server never shows it again (a repeated request answers "already created" without it); a lost password is
 * recovered by an administrator's password reset.
 */
export function TemporaryPasswordPanel({ issued, onDismiss }: { issued: IssuedLogin; onDismiss: () => void }) {
  const [copied, setCopied] = useState<"yes" | "failed" | null>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => headingRef.current?.focus(), []);

  async function copy() {
    try {
      await navigator.clipboard.writeText(issued.temporaryPassword);
      setCopied("yes");
    } catch (err) {
      console.warn("Copying the temporary password failed", err);   // the password stays on screen to be read out instead
      setCopied("failed");
    }
  }

  return (
    <section className="alv-config-panel__form alv-temp-password" role="region" aria-label="Temporary password">
      <h2 ref={headingRef} tabIndex={-1} className="alv-config-panel__form-title">
        Temporary password for {issued.displayName} ({issued.username})
      </h2>
      <p>
        <code className="alv-temp-password__value" data-testid="temporary-password">{issued.temporaryPassword}</code>
      </p>
      <p>Give this password to the person; they must change it when they first sign in.</p>
      <p className="alv-form-field__hint">It is shown only now. If it is lost, issue a password reset in Security Administration.</p>
      {copied === "yes" && <p role="status">Copied.</p>}
      {copied === "failed" && <p role="alert">It could not be copied here. Read it out or write it down instead.</p>}
      <div className="alv-config-panel__actions">
        <Button onClick={() => void copy()}>Copy password</Button>
        <Button variant="primary" onClick={onDismiss}>Done, hide the password</Button>
      </div>
    </section>
  );
}

const LEGACY_ERRORS: Record<string, string> = {
  username_taken: "That username is already in use.",
  username_required: "Enter a username.",
  role_invalid: "Choose a role.",
  staff_has_login: "This staff record already has a login.",
  staff_inactive: "Reactivate this staff record before creating a login.",
  target_is_admin: "Only an administrator can create an administrator login.",
  target_exceeds_actor: "You can only create a login whose permissions you hold yourself.",
};

/**
 * ALV-N030 (R01 CR-1): legacy remediation only. A staff record created before logins were required (active, no login) can be given a NEW login here,
 * with a temporary password, under the same rules as "Add staff member". It never links an existing account.
 */
export function LegacyLoginAction({ row, onIssued }: { row: StaffRecord; onIssued: (issued: IssuedLogin, created: StaffMemberCreated) => Promise<void> }) {
  const [open, setOpen] = useState(false);
  const [username, setUsername] = useState("");
  const [role, setRole] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!row.isActive || row.userAccountId) return null;

  async function submit() {
    if (username.trim() === "" || role === "") {
      setError("Enter a username and choose a role.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const created = await createLegacyLogin(row.id, username.trim(), role);
      setOpen(false);
      setUsername("");
      setRole("");
      if (created.temporaryPassword) await onIssued({ displayName: row.displayName, username: created.username, temporaryPassword: created.temporaryPassword }, created);
      else setError("This login was already created. If its temporary password was lost, issue a password reset in Security Administration.");
    } catch (err) {
      setError(err instanceof ApiError ? LEGACY_ERRORS[err.code] ?? (err.message || "The login could not be created.") : "The login could not be created.");
    } finally {
      setBusy(false);
    }
  }

  if (!open) {
    return (
      <Button onClick={() => setOpen(true)} aria-label={`Create a login for this legacy staff record: ${row.displayName}`}>
        Create a login for this legacy staff record
      </Button>
    );
  }
  return (
    <div role="group" aria-label={`Create a login for ${row.displayName}`} className="alv-legacy-login">
      <label>
        Username
        <input value={username} onChange={(e) => setUsername(e.target.value)} disabled={busy} autoComplete="off" maxLength={256} />
      </label>
      <label>
        Role
        <select value={role} onChange={(e) => setRole(e.target.value)} disabled={busy}>
          <option value="">Select…</option>
          {WORKING_ROLES.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
        </select>
      </label>
      {error && <p role="alert">{error}</p>}
      <Button onClick={() => setOpen(false)} disabled={busy}>Cancel</Button>
      <Button variant="primary" onClick={() => void submit()} disabled={busy}>Create login</Button>
    </div>
  );
}
