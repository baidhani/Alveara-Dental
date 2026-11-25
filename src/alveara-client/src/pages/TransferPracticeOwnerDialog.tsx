import { useEffect, useMemo, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../components/Button";
import { FormField } from "../components/FormField";
import { ApiError } from "../services/authApi";
import type { AdminUserSummary } from "../services/authApi";
import { getPracticeOwner, transferPracticeOwner } from "../services/accountsApi";
import type { PracticeOwner } from "../services/accountsApi";
import { listStaff } from "../services/configApi";
import type { StaffRecord } from "../services/configApi";

/** Stable codes from POST /api/practice-owner/transfer, in this dialog's words. */
const TRANSFER_ERRORS: Record<string, string> = {
  owner_changed: "The Practice Owner changed since this dialog was opened. It now shows the current owner; check it and confirm again.",
  reauthentication_failed: "The password or authenticator code is not correct.",
  mfa_required: "Enter the current code from your authenticator app.",
  account_locked: "Too many failed attempts. Your account is locked for now; try again later.",
  owner_must_be_administrator: "The new Practice Owner must be an active staff member whose login is an enabled Administrator.",
  transfer_same_owner: "This person is already the Practice Owner.",
  transfer_not_permitted: "Only the Deployment Administrator can transfer the Practice Owner designation.",
  no_active_owner: "There is no Practice Owner to transfer from.",
  validation_failed: "Give a reason of 10 to 500 characters.",
  confirmation_required: "Tick the confirmation to transfer.",
};

/**
 * ALV-N030-C01 (brief 18.5, 18.8): the Deployment Administrator's "Transfer Practice Owner" dialog. It shows the current owner and sends that designation's id
 * back as <c>expectedDesignationId</c> (R09-2), so a transfer confirmed against an owner who has since changed is refused (409 owner_changed) instead of moving
 * ownership again. Destinations: active staff members whose login is an enabled Administrator, the current owner excluded. A reason, the password (and the
 * authenticator code when MFA is on), and the confirmation are required; errors keep everything typed. The server is the control for every rule.
 */
export function TransferPracticeOwnerDialog({ users, mfaEnabled, onDone, onCancel }: {
  users: AdminUserSummary[]; mfaEnabled: boolean; onDone: (newOwnerName: string) => void; onCancel: () => void;
}) {
  const [owner, setOwner] = useState<PracticeOwner | null | undefined>(undefined);
  const [staff, setStaff] = useState<StaffRecord[] | null>(null);
  const [loadError, setLoadError] = useState(false);
  const [destination, setDestination] = useState("");
  const [reason, setReason] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [disableFormer, setDisableFormer] = useState(false);
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const key = useRef(crypto.randomUUID());
  const headingRef = useRef<HTMLHeadingElement>(null);

  async function loadOwner() {
    const r = await getPracticeOwner();
    setOwner(r.owner);
  }

  useEffect(() => {
    headingRef.current?.focus();
    let cancelled = false;
    Promise.all([getPracticeOwner(), listStaff(false)])
      .then(([o, s]) => { if (!cancelled) { setOwner(o.owner); setStaff(s); } })
      .catch((err) => { console.warn("Loading the owner or the staff list failed", err); if (!cancelled) setLoadError(true); });
    return () => { cancelled = true; };
  }, []);

  const candidates = useMemo(() => {
    if (!staff || !owner) return [];
    const adminAccounts = new Set(users.filter((u) => u.role === "Admin" && !u.isDisabled && u.accountKind !== "DeploymentAdministrator").map((u) => u.id));
    return staff.filter((s) => s.isActive && s.userAccountId && adminAccounts.has(s.userAccountId) && s.id !== owner.staffProfileId);
  }, [staff, owner, users]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!owner) return;
    setError(null);
    if (reason.trim().length < 10 || reason.trim().length > 500) return setError(TRANSFER_ERRORS.validation_failed);
    if (password === "") return setError("Enter your password.");
    if (mfaEnabled && code.trim() === "") return setError(TRANSFER_ERRORS.mfa_required);
    setBusy(true);
    try {
      await transferPracticeOwner({
        expectedDesignationId: owner.designationId, newOwnerStaffProfileId: destination, reason: reason.trim(), confirm: confirmed,
        currentPassword: password, mfaCode: mfaEnabled ? code.trim() : null, disableFormerOwner: disableFormer,
      }, key.current);
      onDone(candidates.find((c) => c.id === destination)?.displayName ?? "the new owner");
    } catch (err) {
      const codeName = err instanceof ApiError ? err.code : "";
      setError(TRANSFER_ERRORS[codeName] ?? "The transfer could not be completed. Nothing was changed. Please try again.");
      setPassword("");
      setCode("");
      if (codeName === "owner_changed") {
        key.current = crypto.randomUUID();   // a new intent: the next confirmation is a new request
        setConfirmed(false);
        await loadOwner().catch((reloadErr) => console.warn("Reloading the Practice Owner failed", reloadErr));
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="alv-config-panel__form" role="dialog" aria-modal="false" aria-labelledby="transfer-owner-title">
      <h2 id="transfer-owner-title" ref={headingRef} tabIndex={-1} className="alv-config-panel__form-title">Transfer Practice Owner</h2>
      {loadError && <p role="alert">The current owner or the staff list could not be loaded. Close this and try again.</p>}
      {owner === null && <p>There is no Practice Owner to transfer from.</p>}
      {owner && (
        <form onSubmit={submit} noValidate aria-label="Transfer Practice Owner">
          <p>Current Practice Owner: <strong>{owner.displayName}</strong></p>
          {error && <div className="alv-login__banner alv-login__banner--danger" role="alert">{error}</div>}
          <div className="alv-form-field">
            <label className="alv-form-field__label" htmlFor="transfer-destination">New Practice Owner</label>
            <select id="transfer-destination" className="alv-form-field__input" value={destination} onChange={(e) => setDestination(e.target.value)} disabled={busy}>
              <option value="">Select…</option>
              {candidates.map((c) => <option key={c.id} value={c.id}>{c.displayName}</option>)}
            </select>
            <p className="alv-form-field__hint">Only active staff members whose login is an enabled Administrator can be the Practice Owner.</p>
          </div>
          <FormField label="Reason" name="reason" required value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy}
            hint="Why the designation is moving. Do not include patient or personal details." />
          <div className="alv-form-field alv-form-field--checkbox">
            <label className="alv-form-field__label">
              <input type="checkbox" checked={disableFormer} onChange={(e) => setDisableFormer(e.target.checked)} disabled={busy} />{" "}
              Also disable the former owner's login
            </label>
          </div>
          <FormField label="Your password" name="currentPassword" type="password" autoComplete="current-password" required value={password}
            onChange={(e) => setPassword(e.target.value)} disabled={busy} />
          {mfaEnabled && (
            <FormField label="Authenticator code" name="mfaCode" autoComplete="one-time-code" required value={code}
              onChange={(e) => setCode(e.target.value)} disabled={busy} hint="The current code from your authenticator app. Recovery codes are not accepted here." />
          )}
          <div className="alv-form-field alv-form-field--checkbox">
            <label className="alv-form-field__label">
              <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} disabled={busy} />{" "}
              I am transferring the Practice Owner designation to this person.
            </label>
          </div>
          <div className="alv-config-panel__form-actions">
            <Button type="submit" variant="primary" disabled={busy || destination === "" || !confirmed}>{busy ? "Transferring…" : "Transfer"}</Button>
            <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
          </div>
        </form>
      )}
    </section>
  );
}
