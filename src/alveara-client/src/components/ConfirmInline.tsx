import { useEffect, useRef } from "react";
import { Button } from "./Button";

interface Props {
  /** The question, also the dialog's accessible name. */
  title: string;
  /** What will happen, in one or two plain sentences. */
  message: string;
  confirmLabel: string;
  cancelLabel: string;
  onAnswer: (confirmed: boolean) => void;
  busy?: boolean;
}

/**
 * ALV-N025 (F-042): the app's own confirmation, shown in the page instead of the browser's dialog - the same pattern as the staff-inactivation question
 * (ALV-N020): an alert dialog with the safe answer focused, so Enter or a stray click never confirms by accident; Escape cancels.
 */
export function ConfirmInline({ title, message, confirmLabel, cancelLabel, onAnswer, busy }: Props) {
  const cancelRef = useRef<HTMLButtonElement>(null);
  useEffect(() => cancelRef.current?.focus(), []);
  return (
    <div className="alv-config-panel__form" role="alertdialog" aria-label={title} onKeyDown={(e) => { if (e.key === "Escape") onAnswer(false); }}>
      <p><strong>{title}</strong> {message}</p>
      <div className="alv-config-panel__actions">
        <Button ref={cancelRef} type="button" onClick={() => onAnswer(false)} disabled={busy}>{cancelLabel}</Button>
        <Button type="button" variant="danger" onClick={() => onAnswer(true)} disabled={busy}>{confirmLabel}</Button>
      </div>
    </div>
  );
}
