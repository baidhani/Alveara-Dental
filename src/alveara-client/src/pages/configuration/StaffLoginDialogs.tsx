import { useEffect, useRef, useState } from "react";
import { Button } from "../../components/Button";
import { useNotifications } from "../../components/Notification";
import { ApiError } from "../../services/authApi";
import { unlinkStaffAccount } from "../../services/configApi";
import type { StaffRecord } from "../../services/configApi";

/**
 * ALV-N020 (F-021): inactivating a staff profile that has a login also disables that login and ends its sessions, so the person is asked first, in the page
 * (not a browser dialog), with "Keep active" focused.
 */
export function ConfirmStaffInactivation({ row, loginLabel, onAnswer }: { row: StaffRecord; loginLabel: string; onAnswer: (ok: boolean) => void }) {
  const keepRef = useRef<HTMLButtonElement>(null);
  useEffect(() => keepRef.current?.focus(), []);
  return (
    <div className="alv-config-panel__form" role="alertdialog" aria-label={`Inactivate ${row.displayName}?`}>
      <p>
        Inactivating <strong>{row.displayName}</strong> also disables their login ({loginLabel}) and signs them out everywhere. Reactivating the profile later
        does not enable the login again; only an administrator can, in Security Administration.
      </p>
      <div className="alv-config-panel__actions">
        <Button ref={keepRef} onClick={() => onAnswer(false)}>Keep active</Button>
        <Button variant="primary" onClick={() => onAnswer(true)}>Inactivate and disable login</Button>
      </div>
    </div>
  );
}

/** ALV-N020 (F-021): an administrator removes the login link from an INACTIVE staff profile, with a reason that is kept with the record. */
export function UnlinkLoginAction({ row, onDone }: { row: StaffRecord; onDone: () => Promise<void> }) {
  const { notify } = useNotifications();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (row.isActive || !row.userAccountId) return null;

  async function submit() {
    if (reason.trim() === "") {
      setError("Say why the login is being unlinked. The reason is kept with the record.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await unlinkStaffAccount(row.id, reason.trim(), row.rowVersion);
      notify("success", `Unlinked the login from ${row.displayName}.`);
      setOpen(false);
      setReason("");
      await onDone();
    } catch (err) {
      setError(err instanceof ApiError ? err.message || "The login could not be unlinked." : "The login could not be unlinked.");
    } finally {
      setBusy(false);
    }
  }

  if (!open) {
    return <Button onClick={() => setOpen(true)} aria-label={`Unlink login from ${row.displayName}`}>Unlink login</Button>;
  }
  return (
    <div role="group" aria-label={`Unlink login from ${row.displayName}`}>
      <label>
        Reason
        <textarea value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} disabled={busy} />
      </label>
      {error && <p role="alert">{error}</p>}
      <Button onClick={() => setOpen(false)} disabled={busy}>Cancel</Button>
      <Button variant="primary" onClick={() => void submit()} disabled={busy}>Unlink</Button>
    </div>
  );
}
