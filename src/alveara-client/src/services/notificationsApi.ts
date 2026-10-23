/**
 * ALV-N015: typed client for the notification center and internal messages (Controllers/NotificationsController, docs/NOTIFICATIONS.md).
 *
 * - The bell reads `summary` (counts only); a failed read is reported as a failure, never as zero.
 * - A message is sent with an `Idempotency-Key`; the same key is reused for a retry of the same attempt, so a retry can never send it twice.
 * - A list never carries a patient identifier: `hasPatientLink` says a link exists and `patientLink` asks the server to resolve it (a refusal reveals nothing).
 * - Every refusal carries a stable `error` code and, for wrong details, `problems` naming each field.
 */
import { ApiError, fetchCsrfToken, request, requestWithCsrf } from "./authApi";

export type Priority = "Normal" | "Important" | "Urgent";

export interface NotificationSummary { unread: number; unreadImportant: number; unreadUrgent: number; newestBannerAtUtc: string | null }
export interface NotificationView {
  id: string; kind: string; priority: Priority; createdAtUtc: string; readAtUtc: string | null;
  messageId: string | null; senderName: string | null; subject: string | null; hasPatientLink: boolean;
}
export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number }
export interface MessageRecipientView { userId: string; name: string; readAtUtc: string | null }
export interface MessageView {
  id: string; threadId: string; inReplyToMessageId: string | null; senderName: string; isFromMe: boolean; subject: string | null; body: string;
  priority: Priority; sentAtUtc: string; hasPatientLink: boolean; recipients: MessageRecipientView[]; myReadAtUtc: string | null;
}
export interface MessageListItem {
  id: string; threadId: string; senderName: string; isFromMe: boolean; subject: string | null; priority: Priority; sentAtUtc: string;
  recipientCount: number; readCount: number; isUnread: boolean;
}
export interface RecipientChoice { userId: string; name: string; role: string }
export interface Problem { field: string; code: string; message: string }
export interface SendMessageInput { recipientUserIds: string[]; subject: string; body: string; priority: Priority; relatedPatientId?: string | null; inReplyToMessageId?: string | null }
export interface HistoryItem {
  id: string; recipientName: string; kind: string; priority: Priority; status: "Unread" | "Read" | "Hidden"; createdAtUtc: string;
  readAtUtc: string | null; deletedAtUtc: string | null; sourceKey: string | null; messageId: string | null;
}
export interface BreakGlassMessage { id: string; senderName: string; subject: string | null; body: string; priority: Priority; sentAtUtc: string; recipientNames: string[] }
export interface BreakGlassResult { messageId: string | null; threadId: string | null; messages: BreakGlassMessage[] }

/** The wrong details of a refused request, if the server named any. */
export function problemsOf(err: unknown): Problem[] {
  return err instanceof ApiError && Array.isArray(err.body.problems) ? (err.body.problems as Problem[]) : [];
}

export const getSummary = () => request<NotificationSummary>("/api/notifications/summary");
export const listNotifications = (status: "unread" | "history", page = 1) => request<Paged<NotificationView>>(`/api/notifications?status=${status}&page=${page}`);
export const markRead = (id: string) => requestWithCsrf<{ changed: number }>(`/api/notifications/${id}/read`, "POST");
export const markAllRead = () => requestWithCsrf<{ changed: number }>("/api/notifications/read-all", "POST");
export const hideNotification = (id: string) => requestWithCsrf<{ changed: number }>(`/api/notifications/${id}`, "DELETE");
export const clearHistory = () => requestWithCsrf<{ changed: number }>("/api/notifications/history", "DELETE");
export const notificationPatientLink = (id: string) => request<{ path: string }>(`/api/notifications/${id}/patient-link`);
export const messagePatientLink = (id: string) => request<{ path: string }>(`/api/messages/${id}/patient-link`);

export const listMessages = (box: "inbox" | "sent", page = 1) => request<Paged<MessageListItem>>(`/api/messages?box=${box}&page=${page}`);
export const getMessage = (id: string) => request<MessageView>(`/api/messages/${id}`);
export const getThread = (threadId: string) => request<MessageView[]>(`/api/messages/threads/${threadId}`);
export const findRecipients = (search: string) => request<RecipientChoice[]>(`/api/messages/recipients?search=${encodeURIComponent(search)}`);

/** `key` stays the same for a retry of the same attempt (the caller keeps it until the send succeeds or the form changes). */
export async function sendMessage(input: SendMessageInput, key: string): Promise<{ message: MessageView; replayed: boolean }> {
  const csrf = await fetchCsrfToken();
  return request("/api/messages", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-CSRF-Token": csrf, "Idempotency-Key": key },
    body: JSON.stringify(input),
  });
}

export const adminHistory = (status: string, page = 1) => request<Paged<HistoryItem>>(`/api/admin/notifications/history?page=${page}${status ? `&status=${status}` : ""}`);
export const breakGlass = (body: { messageId?: string; threadId?: string; reason: string; confirmed: boolean }) =>
  requestWithCsrf<BreakGlassResult>("/api/admin/messages/break-glass", "POST", body);
