import { useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import type { SchedulingSnapshot } from "../../services/configApi";

interface Props {
  snapshot: SchedulingSnapshot;
  providerId: string;
  operatoryId: string;
  patientName: string;
  busy: boolean;
  onSave: (providerId: string, operatoryId: string) => void;
  onCancel: () => void;
}

/**
 * ALV-011-C01: where the patient is actually being seen during the visit - a different room, or the dentist after the hygienist. It never moves the booking:
 * the calendar still shows the booked provider and operatory. The server decides (a room that holds a seated patient is refused); this only asks.
 */
export function AssignPanel({ snapshot, providerId, operatoryId, patientName, busy, onSave, onCancel }: Props) {
  const [provider, setProvider] = useState(providerId);
  const [operatory, setOperatory] = useState(operatoryId);
  const ids = { provider: useId(), operatory: useId() };
  const unchanged = provider === providerId && operatory === operatoryId;
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!unchanged) onSave(provider, operatory);
  };
  return (
    <form className="flow-assign" onSubmit={submit} aria-label={`Change room or provider for ${patientName}`} noValidate>
      <div className="alv-form-field">
        <label htmlFor={ids.provider} className="alv-form-field__label">Provider</label>
        <select id={ids.provider} className="alv-form-field__input" value={provider} onChange={(e) => setProvider(e.target.value)} disabled={busy}>
          {snapshot.providers.map((p) => <option key={p.providerId} value={p.providerId}>{p.displayName}</option>)}
        </select>
      </div>
      <div className="alv-form-field">
        <label htmlFor={ids.operatory} className="alv-form-field__label">Operatory</label>
        <select id={ids.operatory} className="alv-form-field__input" value={operatory} onChange={(e) => setOperatory(e.target.value)} disabled={busy}>
          {snapshot.operatories.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
        </select>
      </div>
      <p className="alv-workspace__note">This changes where the patient is seen today. The booking on the calendar does not move.</p>
      <div className="flow-actions">
        <Button type="submit" variant="primary" disabled={busy || unchanged}>{busy ? "Saving…" : "Save"}</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}
