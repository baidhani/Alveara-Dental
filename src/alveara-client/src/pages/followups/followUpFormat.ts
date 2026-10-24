import { INTERVAL_MAX, NOTE_MAX, TITLE_MAX } from "../../services/followUpsApi";
import type { FollowUpInput } from "../../services/followUpsApi";

/** STORY-009: small pure helpers for the follow-up screens (date inputs and the checks made before a request is sent; the server checks everything again). */

const pad = (n: number) => String(n).padStart(2, "0");

/** The value of a `datetime-local` input (the person's own clock) for an instant. */
export function toLocalInput(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** The instant (ISO, UTC) a `datetime-local` value means on the person's own clock, or null when it is empty or not a date. */
export function fromLocalInput(value: string): string | null {
  if (!value) return null;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

export const formatWhen = (iso: string) => new Date(iso).toLocaleString();

/** How each kind of history entry reads (the same words in a task's history and in a patient's timeline). */
export const EVENT_LABEL: Record<string, string> = {
  Created: "Created", Edited: "Edited", Reassigned: "Reassigned", DueChanged: "Due date changed", Reminded: "Reminder sent", Completed: "Completed", Cancelled: "Cancelled",
  RecallCreated: "Next follow-up created", Assigned: "Assigned", PriorityChanged: "Priority changed", SystemCreated: "Made by the system", SystemClosed: "Closed by the system",
};

/** The detail of an entry worth showing in words: a due time. Other details are identifiers and are not shown. */
export function dueDetail(detail: string | null): string | null {
  const m = detail?.match(/^Due (.+)$/);
  return m && !Number.isNaN(new Date(m[1]).getTime()) ? `due ${formatWhen(m[1])}` : null;
}

export const actorWords = (name: string | null, id: string | null) => name ?? (id ? "a colleague" : "the system");

export interface FormValues { title: string; note: string; due: string; assignee: string; interval: string }
export type FormErrors = Partial<Record<keyof FormValues, string>>;

export function validate(v: FormValues): FormErrors {
  const e: FormErrors = {};
  const title = v.title.trim();
  if (!title) e.title = "Write a title.";
  else if (title.length > TITLE_MAX) e.title = `Keep the title to ${TITLE_MAX} characters.`;
  else if (/[\r\n\t]/.test(title)) e.title = "The title is one line.";
  if (v.note.trim().length > NOTE_MAX) e.note = `Keep the note to ${NOTE_MAX} characters.`;
  if (!v.due) e.due = "Choose when it is due.";
  else if (fromLocalInput(v.due) === null) e.due = "That is not a date and time.";
  if (!v.assignee) e.assignee = "Choose who is responsible.";
  if (v.interval.trim() !== "") {
    const n = Number(v.interval);
    if (!Number.isInteger(n) || n < 1 || n > INTERVAL_MAX) e.interval = `Enter a whole number of days from 1 to ${INTERVAL_MAX}, or leave it empty.`;
  }
  return e;
}

export function toInput(v: FormValues): FollowUpInput {
  return {
    title: v.title.trim(),
    note: v.note.trim() === "" ? null : v.note.trim(),
    dueAtUtc: fromLocalInput(v.due)!,
    assigneeUserId: v.assignee,
    recallIntervalDays: v.interval.trim() === "" ? null : Number(v.interval),
  };
}
