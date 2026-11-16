import { INTERVAL_MAX, NOTE_MAX, TITLE_MAX } from "../../services/followUpsApi";
import type { FollowUpInput } from "../../services/followUpsApi";
import { localInputText, sessionLocalInput, showInstant } from "../../time/practiceTime";

/** STORY-009: small pure helpers for the follow-up screens (date inputs and the checks made before a request is sent; the server checks everything again). */

/** ALV-N021 A3: the `datetime-local` value for an instant, in the practice zone (empty without one: the person then types it). */
export function toLocalInput(iso: string): string {
  return sessionLocalInput(iso) ?? "";
}

export const formatWhen = (iso: string) => showInstant(iso);

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
  else if (localInputText(v.due) === null) e.due = "That is not a date and time.";
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
    dueAtLocal: localInputText(v.due)!,   // ALV-N021 A3: plain practice-local text; the server converts it
    assigneeUserId: v.assignee,
    recallIntervalDays: v.interval.trim() === "" ? null : Number(v.interval),
  };
}
