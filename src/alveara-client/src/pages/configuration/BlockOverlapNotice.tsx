import { Button } from "../../components/Button";
import type { BlockOverlap } from "../../services/configApi";
import { STATE_LABELS } from "../flow/visitLabels";

interface Props {
  /** "active": the block covers a patient who is already here - it cannot be added at all. "confirm": it covers booked appointments - it can be added with a reason. */
  kind: "active" | "confirm";
  overlaps: BlockOverlap[];
  busy: boolean;
  onConfirm: () => void;
  onBack: () => void;
}

const time = (local: string) => local.replace("T", " ");

/**
 * ALV-N021 A2 (OD-17): what a blocked time would cover, before anything is saved. Over a visit under way the server refuses whatever is confirmed, so this offers
 * no confirm button; over booked appointments it lists them and offers to add the block anyway (the reason in the form is then required), after which they are
 * flagged for the front desk to reschedule, reassign, cancel or keep.
 */
export function BlockOverlapNotice({ kind, overlaps, busy, onConfirm, onBack }: Props) {
  const title = kind === "active" ? "This blocked time covers a patient who is already here" : "This blocked time covers booked appointments";
  return (
    <div className="alv-config-panel__overlap" role="alert" aria-label={title}>
      <p><strong>{title}.</strong></p>
      <p>
        {kind === "active"
          ? "It cannot be added while the visit is under way. Choose another time, or add it once the patient has gone. Nothing was saved."
          : "Add it anyway and these appointments are flagged: the front desk must reschedule, reassign, cancel or keep each one before the patient can be checked in. A reason is required. Nothing has been saved yet."}
      </p>
      <table className="alv-config-panel__table">
        <caption className="alv-visually-hidden">Appointments the blocked time would cover</caption>
        <thead><tr><th scope="col">When</th><th scope="col">Patient</th><th scope="col">Provider</th><th scope="col">Room</th><th scope="col">State</th></tr></thead>
        <tbody>
          {overlaps.map((o) => (
            <tr key={o.appointmentId}>
              <td>{time(o.startLocal)}–{o.endLocal.slice(11)}</td>
              <td>{o.patientName}</td>
              <td>{o.providerName}</td>
              <td>{o.operatoryName}</td>
              <td>{STATE_LABELS[o.flowState as keyof typeof STATE_LABELS] ?? o.flowState}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="alv-config-panel__form-actions">
        {/* type="button": the notice sits inside the blocked-time form, and a submit would re-send the request without the confirmation */}
        {kind === "confirm" && <Button type="button" variant="primary" onClick={onConfirm} disabled={busy}>{busy ? "Adding…" : "Add blocked time and flag these appointments"}</Button>}
        <Button type="button" onClick={onBack} disabled={busy}>Choose another time</Button>
      </div>
    </div>
  );
}
