import { useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import type { SchedulingSnapshot } from "../../services/configApi";
import { MAX_REASON } from "./ReasonForm";

interface Props {
  snapshot: SchedulingSnapshot;
  /** The booked provider and room (what this changes). */
  providerId: string;
  operatoryId: string;
  patientName: string;
  /** From the server's actions: "reassign_provider" listed (otherwise the provider is shown but cannot be changed). */
  allowProvider: boolean;
  /** From the server's actions: "change_room" listed. */
  allowRoom: boolean;
  busy: boolean;
  onSave: (providerId: string, operatoryId: string, reason: string | undefined) => void;
  onCancel: () => void;
}

/**
 * ALV-N021 A2 (OD-13 to OD-16): changes the appointment's BOOKED provider and/or room (it replaces ALV-011-C01's "seen by", which never moved the booking).
 * A provider change needs a reason and notifies both providers; a room-only change does not need one. Only the parts the server listed for this user can be changed;
 * the server checks the booking (double booking, an occupied room) and decides every time.
 */
export function AssignPanel({ snapshot, providerId, operatoryId, patientName, allowProvider, allowRoom, busy, onSave, onCancel }: Props) {
  const [provider, setProvider] = useState(providerId);
  const [operatory, setOperatory] = useState(operatoryId);
  const [reason, setReason] = useState("");
  const [missing, setMissing] = useState(false);
  const ids = { provider: useId(), operatory: useId(), reason: useId(), hint: useId() };
  const providerChanges = provider !== providerId;
  const unchanged = !providerChanges && operatory === operatoryId;
  const title = allowProvider ? `Change provider or room for ${patientName}` : `Change room for ${patientName}`;
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (unchanged) return;
    if (providerChanges && reason.trim() === "") { setMissing(true); return; }
    onSave(provider, operatory, providerChanges ? reason.trim() : undefined);
  };
  return (
    <form className="flow-assign" onSubmit={submit} aria-label={title} noValidate>
      <div className="alv-form-field">
        <label htmlFor={ids.provider} className="alv-form-field__label">Provider</label>
        <select id={ids.provider} className="alv-form-field__input" value={provider} onChange={(e) => setProvider(e.target.value)} disabled={busy || !allowProvider}>
          {snapshot.providers.map((p) => <option key={p.providerId} value={p.providerId}>{p.displayName}</option>)}
        </select>
      </div>
      <div className="alv-form-field">
        <label htmlFor={ids.operatory} className="alv-form-field__label">Operatory</label>
        <select id={ids.operatory} className="alv-form-field__input" value={operatory} onChange={(e) => setOperatory(e.target.value)} disabled={busy || !allowRoom}>
          {snapshot.operatories.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
        </select>
      </div>
      {providerChanges && (
        <div className="alv-form-field">
          <label htmlFor={ids.reason} className="alv-form-field__label">Reason for changing the provider (required)</label>
          <textarea id={ids.reason} className="alv-form-field__input alv-form-inputs__textarea" rows={2} maxLength={MAX_REASON} value={reason} disabled={busy}
            aria-invalid={missing || undefined} aria-describedby={missing ? ids.hint : undefined}
            onChange={(e) => { setReason(e.target.value); if (missing) setMissing(false); }} />
          {missing && <p id={ids.hint} className="alv-form-field__error" role="alert">Give a reason.</p>}
        </div>
      )}
      <p className="alv-workspace__note">
        {providerChanges ? "This moves the booking to the new provider; both providers are notified." : "This moves the booking; the calendar shows the new place."}
      </p>
      <div className="flow-actions">
        <Button type="submit" variant="primary" disabled={busy || unchanged}>{busy ? "Saving…" : "Save"}</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}
