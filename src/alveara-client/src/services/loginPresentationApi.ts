import { ApiError, fetchCsrfToken, request } from "./authApi";
import type { LoginDisplayMode } from "../pages/login/loginBackgrounds";

/** What the sign-in page shows (anonymous; identical for every visitor). */
export interface PublicLoginPresentation {
  background: { kind: "bundled"; key: string } | { kind: "added"; url: string };
  displayMode: LoginDisplayMode;
  footerNotice: string | null;
}

export const PUBLIC_PRESENTATION_TIMEOUT_MS = 10_000;
const ADDED_URL = /^\/api\/public\/login-background\/[0-9a-f]{64}\.(png|jpg)$/;
const MODES = ["Tile", "Stretch", "Fill"];

/**
 * ALV-N020-C01: reads the sign-in presentation without a session and without the app's 401 handling (this page has no session). Anything unexpected in
 * the answer (an unknown shape, a URL that is not our own image route) is treated as "no answer", so the caller keeps the bundled default.
 */
export async function getPublicLoginPresentation(signal?: AbortSignal): Promise<PublicLoginPresentation | null> {
  const res = await fetch("/api/public/login-presentation", { credentials: "same-origin", signal });
  if (!res.ok) return null;
  const body = (await res.json()) as Record<string, unknown>;
  const bg = body.background as Record<string, unknown> | undefined;
  const mode = typeof body.displayMode === "string" && MODES.includes(body.displayMode) ? (body.displayMode as LoginDisplayMode) : null;
  const notice = typeof body.footerNotice === "string" && body.footerNotice.trim().length > 0 ? body.footerNotice : null;
  if (!bg || !mode) return null;
  if (bg.kind === "bundled" && typeof bg.key === "string") return { background: { kind: "bundled", key: bg.key }, displayMode: mode, footerNotice: notice };
  if (bg.kind === "added" && typeof bg.url === "string" && ADDED_URL.test(bg.url)) return { background: { kind: "added", url: bg.url }, displayMode: mode, footerNotice: notice };
  return null;
}

export interface AddedBackground {
  id: string;
  displayName: string;
  width: number;
  height: number;
  suggestedDisplayMode: LoginDisplayMode;
  isActive: boolean;
  addedAtUtc: string;
  rowVersion: string;
}

export interface AdminLoginPresentation {
  backgroundKind: "Bundled" | "Added";
  bundledKey: string | null;
  addedImageId: string | null;
  displayMode: LoginDisplayMode;
  footerNoticeText: string | null;
  showFooterNotice: boolean;
  deploymentDefaultNotice: string | null;
  bundled: { key: string; displayName: string; tileAllowed: boolean; suggestedDisplayMode: LoginDisplayMode }[];
  added: AddedBackground[];
  maxActiveAdded: number;
  rowVersion: string;
}

export interface SaveLoginPresentation {
  backgroundKind: "Bundled" | "Added";
  bundledKey: string | null;
  addedImageId: string | null;
  displayMode: LoginDisplayMode;
  footerNoticeText: string | null;
  useDefaultNotice: boolean;
  showFooterNotice: boolean;
  rowVersion: string;
}

async function send<T>(url: string, method: string, body: BodyInit, json: boolean): Promise<T> {
  const csrf = await fetchCsrfToken();
  const res = await fetch(url, {
    method,
    credentials: "include",
    headers: { "X-CSRF-Token": csrf, ...(json ? { "Content-Type": "application/json" } : {}) },
    body,
  });
  const text = await res.text();
  const parsed = text ? (JSON.parse(text) as Record<string, unknown>) : {};
  if (!res.ok) throw new ApiError(res.status, String(parsed.error ?? "unknown_error"), String(parsed.message ?? parsed.title ?? `Request failed with status ${res.status}.`), parsed);
  return parsed as T;
}

export const getAdminLoginPresentation = () => request<AdminLoginPresentation>("/api/config/login-presentation");

export const saveLoginPresentation = (input: SaveLoginPresentation) =>
  send<AdminLoginPresentation>("/api/config/login-presentation", "PUT", JSON.stringify(input), true);

export function addLoginBackground(file: File, displayName: string, suggestedDisplayMode: LoginDisplayMode, publicAcknowledged: boolean) {
  const form = new FormData();
  form.append("file", file, file.name);
  form.append("displayName", displayName);
  form.append("suggestedDisplayMode", suggestedDisplayMode);
  form.append("publicAcknowledged", publicAcknowledged ? "true" : "false");
  return send<AddedBackground>("/api/config/login-presentation/backgrounds", "POST", form, false);
}

export const setLoginBackgroundActive = (id: string, isActive: boolean, rowVersion: string) =>
  send<AddedBackground>(`/api/config/login-presentation/backgrounds/${id}/active`, "PUT", JSON.stringify({ isActive, rowVersion }), true);

export const adminBackgroundImageUrl = (id: string) => `/api/config/login-presentation/backgrounds/${id}/image`;

export const getPresentationAdministrator = (accountId: string) => request<{ granted: boolean }>(`/api/auth/${accountId}/presentation-administrator`);

export const setPresentationAdministrator = (accountId: string, granted: boolean) =>
  send<{ granted: boolean; changed: boolean }>(`/api/auth/${accountId}/presentation-administrator`, "PUT", JSON.stringify({ granted }), true);
