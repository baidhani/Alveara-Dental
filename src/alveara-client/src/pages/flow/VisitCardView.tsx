import { useState } from "react";
import { Button } from "../../components/Button";
import type { SchedulingSnapshot } from "../../services/configApi";
import { dateOf, timeOf } from "../../services/schedulingApi";
import type { PatientFlowState } from "../../services/schedulingApi";
import type { VisitAction, VisitCard } from "../../services/visitsApi";
import { statusWord } from "../calendar/calendarLayout";
import { AssignPanel } from "./AssignPanel";
import { CheckoutPanel } from "./CheckoutPanel";
import { ReadinessCue } from "./ReadinessCue";
import { ReasonForm } from "./ReasonForm";
import { elapsedCue } from "./visitTime";

interface Props {
  card: VisitCard;
  nowMs: number;
  snapshot: SchedulingSnapshot;
  busy: boolean;
  error: string | null;
  onMove: (card: VisitCard, target: PatientFlowState) => void;
  onAssign: (card: VisitCard, providerId: string, operatoryId: string, reason: string | undefined) => void;
  onUndoCheckIn: (card: VisitCard, reason: string) => void;
  onKeep: (card: VisitCard, reason: string) => void;
  /** A checkout outcome was recorded: re-read this card's actions (Complete may now be enabled). */
  onCheckoutRecorded: (card: VisitCard) => void;
}

type Open = { kind: "none" } | { kind: "complete"; target: PatientFlowState } | { kind: "assign" } | { kind: "undo" } | { kind: "keep" };

/** The visit-flow actions the board offers; reschedule, cancel and no-show are done in the calendar. */
const BOARD_CODES = new Set(["confirm", "check_in", "mark_ready", "seat", "start_treatment", "end_treatment", "complete", "undo_check_in", "keep_despite_block"]);

/**
 * ALV-011-C01: one visit on the live board, showing only what the server stored. ALV-N021 A2: its buttons come ONLY from the server's list of actions for the signed-in
 * user (`card.actions`, the same as GET appointments/{id}/actions), never from rules of its own. An action the user may take but that a rule blocks right now (check-in on
 * blocked time, completion with open checkout items) stays visible but disabled, with the server's reason beside it. The card also says who the visit waits for.
 * Completing is final, so it asks first; undo check-in, keep and a provider change ask for a reason.
 */
export function VisitCardView({ card, nowMs, snapshot, busy, error, onMove, onAssign, onUndoCheckIn, onKeep, onCheckoutRecorded }: Props) {
  const a = card.appointment;
  const [open, setOpen] = useState<Open>({ kind: "none" });
  const [checkoutShown, setCheckoutShown] = useState(false);
  const close = () => setOpen({ kind: "none" });
  const scheduled = a.status === "Scheduled";
  const flow = a.flowState ?? "Scheduled";
  const list = card.actions?.actions ?? [];
  const shown = list.filter((x) => BOARD_CODES.has(x.code));
  const allowProvider = list.some((x) => x.code === "reassign_provider");
  const allowRoom = list.some((x) => x.code === "change_room");
  const where = `${a.visitProviderName ?? a.providerName} · ${a.visitOperatoryName ?? a.operatoryName}`;
  const moved = (a.visitProviderId && a.visitProviderId !== a.providerId) || (a.visitOperatoryId && a.visitOperatoryId !== a.operatoryId);
  const cue = elapsedCue(a, nowMs);
  const titleId = `visit-title-${a.id}`;
  const flagged = card.actions?.blockFlagged ?? a.blockFlagged ?? false;

  const press = (x: VisitAction) => {
    if (x.code === "complete" && x.targetState) setOpen({ kind: "complete", target: x.targetState });
    else if (x.code === "undo_check_in") setOpen({ kind: "undo" });
    else if (x.code === "keep_despite_block") setOpen({ kind: "keep" });
    else if (x.targetState) onMove(card, x.targetState);
  };

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
      {flagged && <p className="flow-card__blocked" data-block-flag="true"><strong>Blocked time</strong> covers this appointment.</p>}
      {card.carriedOver && <p className="flow-card__carried">Carried over from {dateOf(a.startLocal)} and still open.</p>}
      {cue && <p className="flow-card__elapsed">{cue}</p>}
      {card.readiness && <ReadinessCue readiness={card.readiness} />}
      {card.safety && (
        <p className="flow-card__safety" data-safety="true">
          {card.safety.alert && <span className="flow-card__safety-flag">Safety alert on file</span>}
          {card.safety.alert && card.safety.clearance && " · "}
          {card.safety.clearance && <span className="flow-card__safety-flag">Clearance open</span>}
        </p>
      )}
      {scheduled && card.actions?.nextActorText && <p className="flow-card__next">{card.actions.nextActorText}</p>}
      {error && <p className="flow-card__error" role="alert">{error}</p>}
      {scheduled && flow === "CheckedOut" && (
        <>
          <Button aria-expanded={checkoutShown} aria-label={`${checkoutShown ? "Hide" : "Show"} checkout for ${a.patientName}`} onClick={() => setCheckoutShown((s) => !s)}>
            {checkoutShown ? "Hide checkout" : "Checkout"}
          </Button>
          {checkoutShown && <CheckoutPanel appointmentId={a.id} patientName={a.patientName} onRecorded={() => onCheckoutRecorded(card)} />}
        </>
      )}

      {open.kind === "complete" ? (
        <div className="flow-confirm" role="group" aria-label={`Confirm: Complete visit for ${a.patientName}`}>
          <p>Complete the visit for {a.patientName}? This cannot be undone.</p>
          <div className="flow-actions">
            <Button variant="primary" disabled={busy} onClick={() => { const t = open.target; close(); onMove(card, t); }}>Yes, complete the visit</Button>
            <Button disabled={busy} onClick={close}>Keep it open</Button>
          </div>
        </div>
      ) : open.kind === "undo" ? (
        <ReasonForm title={`Undo check-in for ${a.patientName}`} explanation="The patient goes back to the state before check-in. The check-in stays in the history."
          submitLabel="Undo check-in" busy={busy} onSubmit={(r) => { close(); onUndoCheckIn(card, r); }} onCancel={close} />
      ) : open.kind === "keep" ? (
        <ReasonForm title={`Keep the appointment for ${a.patientName}`} explanation="The appointment stays where it is despite the provider's blocked time, and can be checked in."
          submitLabel="Keep appointment" busy={busy} onSubmit={(r) => { close(); onKeep(card, r); }} onCancel={close} />
      ) : open.kind === "assign" ? (
        <AssignPanel snapshot={snapshot} providerId={a.providerId} operatoryId={a.operatoryId} patientName={a.patientName} allowProvider={allowProvider} allowRoom={allowRoom}
          busy={busy} onSave={(p, o, r) => { close(); onAssign(card, p, o, r); }} onCancel={close} />
      ) : (
        (shown.length > 0 || allowProvider || allowRoom) && (
          <div className="flow-actions">
            {shown.map((x, i) => {
              const hintId = `visit-${a.id}-${x.code}-blocked`;
              return (
                <span key={x.code} className="flow-action">
                  <Button variant={i === 0 && x.enabled ? "primary" : "secondary"} disabled={busy || !x.enabled} aria-label={`${x.label} ${a.patientName}`}
                    aria-describedby={x.enabled ? undefined : hintId} onClick={() => press(x)}>
                    {x.label}
                  </Button>
                  {!x.enabled && x.blockedText && <span id={hintId} className="flow-action__blocked">{x.blockedText}</span>}
                </span>
              );
            })}
            {(allowProvider || allowRoom) && (
              <Button disabled={busy} aria-label={`${allowProvider ? "Change provider or room" : "Change room"} for ${a.patientName}`} onClick={() => setOpen({ kind: "assign" })}>
                {allowProvider ? "Change provider or room" : "Change room"}
              </Button>
            )}
          </div>
        )
      )}
    </article>
  );
}
