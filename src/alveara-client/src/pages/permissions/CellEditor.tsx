import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { isStaleWrite, newPolicyKey, policyErrorMessage, setOverride } from "../../services/permissionPolicyApi";
import type { EffectiveCell, EffectiveRole } from "../../services/permissionPolicyApi";
import "./permissions.css";

export const REASON_MAX = 500;

/**
 * ALV-N018: changes one cell of the permission matrix. It shows what the change does (how many enabled accounts hold the role, what it becomes), needs a written reason and an explicit confirmation, and for the
 * high-impact permissions the permission name typed exactly. The `Idempotency-Key` is made once per attempt and kept for a retry after a failure (so pressing Save again cannot save twice); editing the form starts
 * a new attempt. A stale write (the policy moved while the form was open) is explained and offers a reload; nothing is stored.
 */
export function CellEditor({ role, cell, version, onSaved, onCancel, onStale }: {
  role: EffectiveRole; cell: EffectiveCell; version: number;
  onSaved: (message: string) => void; onCancel: () => void; onStale: () => void;
}) {
  const effect = cell.source !== "default" ? "Revert" : cell.effective ? "Revoke" : "Grant";
  const [reason, setReason] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [typed, setTyped] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);
  const [busy, setBusy] = useState(false);
  const key = useRef(newPolicyKey());
  const heading = useRef<HTMLHeadingElement>(null);
  const changed = () => { key.current = newPolicyKey(); };

  useEffect(() => { heading.current?.focus(); }, []);

  const after = effect === "Revert" ? "its default" : effect === "Grant" ? "granted" : "revoked";
  const typedOk = !cell.requiresTypedConfirmation || typed === cell.permission;
  const ready = reason.trim().length > 0 && reason.length <= REASON_MAX && confirmed && typedOk;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy || !ready) return;
    setBusy(true); setError(null);
    try {
      const result = await setOverride({ role: role.role, permission: cell.permission, effect, reason: reason.trim(), expectedVersion: version, confirmed, typedConfirmation: cell.requiresTypedConfirmation ? typed : undefined }, key.current);
      key.current = newPolicyKey();
      onSaved(`${cell.permission} for ${role.role}: ${result.change.toLowerCase()}. It applies to the next request.`);
    } catch (err) {
      if (isStaleWrite(err)) setStale(true);
      setError(policyErrorMessage(err));
    } finally { setBusy(false); }
  }

  return (
    <form className="alv-policy-editor" onSubmit={submit} aria-labelledby="alv-policy-editor-title" noValidate>
      <h2 id="alv-policy-editor-title" tabIndex={-1} ref={heading}>{effect === "Revert" ? "Revert" : effect} {cell.permission} for {role.role}</h2>
      <p>
        Now: <strong>{cell.effective ? "granted" : "not granted"}</strong> ({cell.source}). After: <strong>{after}</strong>. This changes access for <strong>{role.enabledUsers}</strong> enabled
        account{role.enabledUsers === 1 ? "" : "s"} with the {role.role} role, from their next request.
      </p>
      {error && <p role="alert" className="alv-policy-error">{error}</p>}
      {stale && <Button type="button" onClick={onStale}>Reload the policy</Button>}
      <div className="alv-form-field">
        <label htmlFor="alv-policy-reason" className="alv-form-field__label">Reason (required, up to {REASON_MAX} characters)</label>
        <textarea id="alv-policy-reason" rows={3} maxLength={REASON_MAX} value={reason} onChange={(e) => { changed(); setReason(e.target.value); }} />
      </div>
      {cell.requiresTypedConfirmation && (
        <div className="alv-form-field">
          <label htmlFor="alv-policy-typed" className="alv-form-field__label">This is a high-impact permission. Type {cell.permission} to confirm</label>
          <input id="alv-policy-typed" className="alv-form-field__input" autoComplete="off" value={typed} onChange={(e) => { changed(); setTyped(e.target.value); }} />
        </div>
      )}
      <label className="alv-policy-check">
        <input type="checkbox" checked={confirmed} onChange={(e) => { changed(); setConfirmed(e.target.checked); }} /> I understand this takes effect immediately and is recorded with my name and reason
      </label>
      <div className="alv-policy-actions">
        <Button type="submit" variant="primary" disabled={busy || !ready}>{busy ? "Saving…" : effect === "Revert" ? "Revert to default" : `Save: ${effect.toLowerCase()}`}</Button>
        <Button type="button" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  );
}
