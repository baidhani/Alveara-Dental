import { useId, useState } from "react";
import type { PrescriptionRecord } from "../../../services/prescriptionWorkflowApi";
import { when } from "../../treatmentplan/treatmentPlanRules";
import { allergyResultWords, doseWords } from "../prescriptionText";
import { quantityWords, statusWords } from "./workflowRules";

interface Props {
  records: PrescriptionRecord[];
  /** Whether the signed-in person may change a prescription. It only decides which buttons are drawn (the server checks every call). */
  canPrescribe: boolean;
  onPrint: (id: string) => void;
  onAmend: (id: string) => void;
  /** Ends a prescription with a reason. Resolves to a message to show when it was refused, or null when it was recorded. */
  onEnd: (kind: "cancel" | "discontinue", id: string, reason: string) => Promise<string | null>;
}

/**
 * ALV-016-C01: the patient's finalized prescriptions, newest first, each with its status, what finalizing recorded (instructions, quantity, refills), who prescribed it and when, what the allergy check said at the time
 * and its history. A prescription is never edited or deleted here: it can be printed, amended (a new draft that replaces it when finalized), cancelled or discontinued (each with a reason that is kept).
 * One saved before instructions, quantity and refills were recorded says so, and cannot be printed until it is amended.
 */
export function PrescriptionRecordList({ records, canPrescribe, onPrint, onAmend, onEnd }: Props) {
  const uid = useId();
  const [ending, setEnding] = useState<{ id: string; kind: "cancel" | "discontinue" } | null>(null);
  const [reason, setReason] = useState("");
  const [refusal, setRefusal] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function confirmEnd() {
    if (!ending) return;
    setBusy(true);
    const refused = await onEnd(ending.kind, ending.id, reason);
    setBusy(false);
    setRefusal(refused);
    if (refused === null) { setEnding(null); setReason(""); }
  }

  if (records.length === 0) return <p className="alv-clinical__meta">No finalized prescription has been recorded for this patient. That means none has been recorded here, not that none was ever given.</p>;
  return (
    <ul className="alv-rx__list" aria-label="Finalized prescriptions">
      {records.map((p) => (
        <li key={p.id} className="alv-rx__item" data-status={p.status}>
          <h4 className="alv-rx__medication">{p.medication}</h4>
          <p>{doseWords(p)} · {p.frequency}</p>
          <p><strong>Status:</strong> {statusWords(p.status)}</p>
          {p.hasDetails ? (
            <>
              <p className="alv-rx__note">{p.instructions}</p>
              <p>Quantity {quantityWords(p.quantityValue, p.quantityUnit)} · Refills {p.refills}</p>
            </>
          ) : <p className="alv-clinical__meta">Saved before instructions, quantity and refills were recorded. Amend it to add them; it cannot be printed until then.</p>}
          <p className="alv-clinical__meta">Prescribed by {p.prescribedByName ?? "an unnamed user"} on {when(p.prescribedAtUtc)}.</p>
          <p className="alv-clinical__meta">{allergyResultWords(p)}</p>
          {p.reviewNote && <p className="alv-rx__note"><strong>Prescriber&apos;s review:</strong> {p.reviewNote}</p>}
          {p.events.length > 0 && (
            <details>
              <summary>History</summary>
              <ul aria-label={`History of ${p.medication}`}>
                {p.events.map((e, i) => <li key={i}>{e.eventType} by {e.actorName ?? "an unnamed user"} on {when(e.occurredAtUtc)}{e.reason ? `: ${e.reason}` : ""}</li>)}
              </ul>
            </details>
          )}
          <div className="alv-clinical__row-actions">
            {p.hasDetails && <button type="button" className="btn btn-outline-primary btn-sm" onClick={() => onPrint(p.id)}>Print {p.medication}</button>}
            {canPrescribe && p.status === "Active" && (
              <>
                <button type="button" className="btn btn-outline-primary btn-sm" onClick={() => onAmend(p.id)}>Amend {p.medication}</button>
                <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => { setEnding({ id: p.id, kind: "cancel" }); setRefusal(null); }}>Cancel {p.medication}</button>
                <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => { setEnding({ id: p.id, kind: "discontinue" }); setRefusal(null); }}>Discontinue {p.medication}</button>
              </>
            )}
          </div>
          {ending?.id === p.id && (
            <div className="alv-rx__end">
              <label htmlFor={`${uid}-${p.id}`}>{ending.kind === "cancel" ? "Why is this prescription being cancelled? (kept with it)" : "Why is this prescription being discontinued? (kept with it)"}</label>
              <textarea id={`${uid}-${p.id}`} rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
              {refusal && <p role="alert" className="alv-rx__error">{refusal}</p>}
              <div className="alv-clinical__row-actions">
                <button type="button" className="btn btn-primary btn-sm" disabled={busy} onClick={() => void confirmEnd()}>{ending.kind === "cancel" ? "Confirm cancellation" : "Confirm discontinuation"}</button>
                <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy} onClick={() => { setEnding(null); setReason(""); setRefusal(null); }}>Keep it active</button>
              </div>
            </div>
          )}
        </li>
      ))}
    </ul>
  );
}
