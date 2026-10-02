import { useId } from "react";
import { FormField } from "../../components/FormField";
import type { SchedulingSnapshot } from "../../services/configApi";

export interface Placement {
  providerId: string;
  operatoryId: string;
  date: string;
  time: string;
  duration: string;
}

interface Props {
  snapshot: SchedulingSnapshot;
  values: Placement;
  onChange: (next: Placement) => void;
  disabled?: boolean;
  durationHint?: string;
}

/** Where and when: provider, operatory, date, start time and an optional duration - the part of a booking or reschedule that is the same in both. */
export function PlacementFields({ snapshot, values, onChange, disabled, durationHint }: Props) {
  const ids = { provider: useId(), operatory: useId() };
  const set = (patch: Partial<Placement>) => onChange({ ...values, ...patch });
  return (
    <div className="alv-schedule__grid">
      <div className="alv-form-field">
        <label htmlFor={ids.provider} className="alv-form-field__label">Provider *</label>
        <select id={ids.provider} className="alv-form-field__input" value={values.providerId} onChange={(e) => set({ providerId: e.target.value })} disabled={disabled}>
          <option value="">Choose…</option>
          {snapshot.providers.map((p) => <option key={p.providerId} value={p.providerId}>{p.displayName}</option>)}
        </select>
      </div>
      <div className="alv-form-field">
        <label htmlFor={ids.operatory} className="alv-form-field__label">Operatory *</label>
        <select id={ids.operatory} className="alv-form-field__input" value={values.operatoryId} onChange={(e) => set({ operatoryId: e.target.value })} disabled={disabled}>
          <option value="">Choose…</option>
          {snapshot.operatories.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
        </select>
      </div>
      <FormField label="Date *" type="date" value={values.date} onChange={(e) => set({ date: e.target.value })} disabled={disabled} />
      <FormField label="Start time *" type="time" step={300} value={values.time} onChange={(e) => set({ time: e.target.value })} disabled={disabled} />
      <FormField label="Duration in minutes (optional)" type="number" min={5} max={480} step={5} value={values.duration} onChange={(e) => set({ duration: e.target.value })} disabled={disabled} hint={durationHint} />
    </div>
  );
}
