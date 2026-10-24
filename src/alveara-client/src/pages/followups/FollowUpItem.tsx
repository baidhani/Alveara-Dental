import { useState } from "react";
import { Button } from "../../components/Button";
import type { FollowUp } from "../../services/followUpsApi";
import { kindLabel } from "../../services/workQueueApi";
import { FollowUpHistory } from "./FollowUpHistory";
import { formatWhen } from "./followUpFormat";
import "./followups.css";

interface Props {
  task: FollowUp;
  /** Whether the signed-in person may change follow-ups. It only decides which buttons are drawn (the server checks every call). */
  canManage: boolean;
  onEdit: (task: FollowUp) => void;
  onComplete: (task: FollowUp) => void;
  onCancel: (task: FollowUp) => void;
}

/** STORY-009: one follow-up in a list: what, when it is due (and that it is overdue, in words, not only in colour), who is responsible, its state and its actions and history. */
export function FollowUpItem({ task, canManage, onEdit, onComplete, onCancel }: Props) {
  const [showHistory, setShowHistory] = useState(false);
  const open = task.status === "Open";
  return (
    <li className="alv-fu-item">
      <p className="alv-fu-item__title">
        <strong>{task.title}</strong>{" "}
        <span className={`alv-fu-badge alv-fu-badge--${task.status.toLowerCase()}`}>{task.status}</span>
        {open && task.overdue && <span className="alv-fu-badge alv-fu-badge--overdue">Overdue</span>}
        {task.priority !== "Normal" && <span className={`alv-fu-badge alv-fu-badge--${task.priority.toLowerCase()}`}>{task.priority} priority</span>}
        {task.kind !== "Manual" && <span className="alv-fu-badge">{kindLabel(task.kind)}</span>}
      </p>
      <p className="alv-fu-item__meta">
        Due {formatWhen(task.dueAtUtc)} · Responsible: {task.assigneeUserId === null ? "nobody yet (unassigned)" : task.assigneeName ?? "a former colleague"}
        {task.appointmentId ? " · Linked to an appointment" : ""}
        {task.recallIntervalDays ? ` · Repeats ${task.recallIntervalDays} day${task.recallIntervalDays === 1 ? "" : "s"} after completion` : ""}
        {task.recallOfTaskId ? " · Made by completing an earlier follow-up" : ""}
      </p>
      {task.note && <p className="alv-fu-item__note">{task.note}</p>}
      {!open && task.closedAtUtc && (
        <p className="alv-fu-item__meta">
          {task.status} {formatWhen(task.closedAtUtc)}{task.closureNote ? `: ${task.closureNote}` : ""}
        </p>
      )}
      <div className="alv-fu-actions">
        {open && canManage && (
          <>
            <Button type="button" onClick={() => onComplete(task)} aria-label={`Complete ${task.title}`}>Complete</Button>
            <Button type="button" onClick={() => onEdit(task)} aria-label={`Edit ${task.title}`}>Edit</Button>
            <Button type="button" variant="danger" onClick={() => onCancel(task)} aria-label={`Cancel ${task.title}`}>Cancel</Button>
          </>
        )}
        <Button type="button" aria-expanded={showHistory} onClick={() => setShowHistory((s) => !s)} aria-label={`${showHistory ? "Hide history" : "History"} of ${task.title}`}>
          {showHistory ? "Hide history" : "History"}
        </Button>
      </div>
      {showHistory && <FollowUpHistory taskId={task.id} version={task.rowVersion} />}
    </li>
  );
}
