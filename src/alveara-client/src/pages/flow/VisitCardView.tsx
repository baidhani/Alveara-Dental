import { useState } from "react";
import { Button } from "../../components/Button";
import type { SchedulingSnapshot } from "../../services/configApi";
import { dateOf, timeOf } from "../../services/schedulingApi";
import type { PatientFlowState } from "../../services/schedulingApi";
import type { VisitCard } from "../../services/visitsApi";
import { statusWord } from "../calendar/calendarLayout";
import { AssignPanel } from "./AssignPanel";
import { ReadinessCue } from "./ReadinessCue";
import { ACTION_LABELS, needsConfirmation, permissionFor } from "./visitLabels";
import { elapsedCue } from "./visitTime";

interface Props {
  card: VisitCard;
  nowMs: number;
  snapshot: SchedulingSnapshot;
  /** What this person may do (the server decides every time; this only decides which buttons to offer). */
  can: (permission: string) => boolean;
  busy: boolean;
  error: string | null;
  onMove: (card: VisitCard, target: PatientFlowState) => void;
  onAssign: (card: VisitCard, providerId: string, operatoryId: string) => void;
}

/**
 * ALV-011-C01: one visit on the live board. It shows only what the server stored: the patient, the time, WHERE the patient actually is (and, when that
 * differs, where the visit was booked), how long they have been in this step, the check-in form cue, and the moves the server says come next - limited to the
 * ones this person's role may make. Completing a visit is final, so it alone asks for confirmation; every other move is a single press.
 */
export function VisitCardView({ card, nowMs, snapshot, can, busy, error, onMove, onAssign }: Props) {
  const a = card.appointment;
  const [confirming, setConfirming] = useState<PatientFlowState | null>(null);
  const [assigning, setAssigning] = useState(false);
  const scheduled = a.status === "Scheduled";
  const flow = a.flowState ?? "Scheduled";
  const moves = scheduled ? (a.nextFlowStates ?? []).filter((t) => can(permissionFor(t))) : [];
  const canAssign = scheduled && flow !== "Completed" && (can("UpdateVisitFlow") || can("UpdateChairsideFlow"));
  const where = `${a.visitProviderName ?? a.providerName} · ${a.visitOperatoryName ?? a.operatoryName}`;
  const moved = (a.visitProviderId && a.visitProviderId !== a.providerId) || (a.visitOperatoryId && a.visitOperatoryId !== a.operatoryId);
  const cue = elapsedCue(a, nowMs);
  const titleId = `visit-title-${a.id}`;

  return (
    <article className={`flow-card flow-card--${a.status.toLowerCase()}`} aria-labelledby={titleId} data-visit={a.id} data-flow={flow} data-status={a.status}>
      <p className="flow-card__time">
        {timeOf(a.startLocal)}–{timeOf(a.endLocal)}
        {!scheduled && <strong className="flow-card__status"> · {statusWord(a.status)}</strong>}
      </p>
      <h3 id={titleId} className="flow-card__patient" tabIndex={-1}>{a.patientName}</h3>
      <p className="flow-card__meta">{a.appointmentTypeName}</p>
      <p className="flow-card__where">{where}</p>
      {moved && <p className="flow-card__booked">Booked: {a.providerName} · {a.operatoryName}</p>}
      {card.carriedOver && <p className="flow-card__carried">Carried over from {dateOf(a.startLocal)} and still open.</p>}
      {cue && <p className="flow-card__elapsed">{cue}</p>}
      {card.readiness && <ReadinessCue readiness={card.readiness} />}
      {error && <p className="flow-card__error" role="alert">{error}</p>}

      {confirming ? (
        <div className="flow-confirm" role="group" aria-label={`Confirm: ${ACTION_LABELS[confirming]} for ${a.patientName}`}>
          <p>Complete the visit for {a.patientName}? This cannot be undone.</p>
          <div className="flow-actions">
            <Button variant="primary" disabled={busy} onClick={() => { const t = confirming; setConfirming(null); onMove(card, t); }}>Yes, complete the visit</Button>
            <Button disabled={busy} onClick={() => setConfirming(null)}>Keep it open</Button>
          </div>
        </div>
      ) : assigning ? (
        <AssignPanel
          snapshot={snapshot}
          providerId={a.visitProviderId ?? a.providerId}
          operatoryId={a.visitOperatoryId ?? a.operatoryId}
          patientName={a.patientName}
          busy={busy}
          onSave={(p, o) => { setAssigning(false); onAssign(card, p, o); }}
          onCancel={() => setAssigning(false)}
        />
      ) : (
        (moves.length > 0 || canAssign) && (
          <div className="flow-actions">
            {moves.map((target, i) => (
              <Button
                key={target}
                variant={i === 0 ? "primary" : "secondary"}
                disabled={busy}
                aria-label={`${ACTION_LABELS[target]} ${a.patientName}`}
                onClick={() => (needsConfirmation(target) ? setConfirming(target) : onMove(card, target))}
              >
                {ACTION_LABELS[target]}
              </Button>
            ))}
            {canAssign && <Button disabled={busy} aria-label={`Change room or provider for ${a.patientName}`} onClick={() => setAssigning(true)}>Change room or provider</Button>}
          </div>
        )
      )}
    </article>
  );
}
