/**
 * ALV-001-C01: thin typed client for the auth/security-administration API. Every call is
 * same-origin cookie auth (credentials: "include" is the default for same-origin fetches, but set
 * explicitly since this can run behind the dev proxy) — no bearer tokens, no client-held secrets.
 * State-changing calls attach the CSRF header the server's double-submit check requires.
 */

export interface AccountResponse {
  id: string;
  username: string;
  role: string;
}

export interface AdminUserSummary {
  id: string;
  username: string;
  role: string;
  isDisabled: boolean;
  mfaEnabled: boolean;
  sessionTimeoutMinutes: number;
  createdAtUtc: string;
}

export interface MyPermissions {
  role: string;
  permissions: string[];
}

export interface PermissionMatrixEntry {
  role: string;
  permissions: string[];
}

export interface MfaEnrollmentResult {
  base32Secret: string;
  recoveryCodes: string[];
}

export class ApiError extends Error {
  status: number;
  code: string;
  /** The full parsed error body, for endpoint-specific fields (e.g. login's lockedUntilUtc). */
  body: Record<string, unknown>;

  constructor(status: number, code: string, message: string, body: Record<string, unknown> = {}) {
    super(message);
    this.status = status;
    this.code = code;
    this.body = body;
  }
}

async function parseErrorBody(res: Response): Promise<{ error: string; message?: string; body: Record<string, unknown> }> {
  try {
    const body = (await res.json()) as Record<string, unknown>;
    return { error: (body.error as string) ?? "unknown_error", message: body.message as string | undefined, body };
  } catch {
    return { error: "unknown_error", body: {} };
  }
}

async function request<T>(input: RequestInfo, init?: RequestInit): Promise<T> {
  const res = await fetch(input, { credentials: "include", ...init });
  if (!res.ok) {
    const { error, message, body } = await parseErrorBody(res);
    throw new ApiError(res.status, error, message ?? `Request failed with status ${res.status}.`, body);
  }
  if (res.status === 204) return undefined as T;
  // Some endpoints (e.g. mfa/confirm, logout) return 200 with an empty body rather than 204 -
  // reading as text first avoids res.json() throwing "Unexpected end of JSON input" on those.
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export async function fetchCsrfToken(): Promise<string> {
  const body = await request<{ token: string }>("/api/auth/csrf-token");
  return body.token;
}

async function requestWithCsrf<T>(url: string, method: string, body?: unknown): Promise<T> {
  const csrfToken = await fetchCsrfToken();
  return request<T>(url, {
    method,
    headers: { "Content-Type": "application/json", "X-CSRF-Token": csrfToken },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
}

/** A 202 from the server means "password verified, MFA code required next" — not a failure. */
export interface MfaRequiredResult {
  mfaRequired: true;
  challengeToken: string;
}

export async function login(username: string, password: string): Promise<AccountResponse | MfaRequiredResult> {
  const res = await fetch("/api/auth/login", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password }),
  });

  if (res.status === 202) {
    const body = (await res.json()) as { challengeToken: string };
    return { mfaRequired: true, challengeToken: body.challengeToken };
  }
  if (!res.ok) {
    const { error, message, body } = await parseErrorBody(res);
    throw new ApiError(res.status, error, message ?? `Login failed with status ${res.status}.`, body);
  }
  return (await res.json()) as AccountResponse;
}

export async function completeMfaChallenge(challengeToken: string, code: string): Promise<AccountResponse> {
  return request<AccountResponse>("/api/auth/mfa/challenge", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ challengeToken, code }),
  });
}

export async function completePasswordReset(userId: string, token: string, newPassword: string): Promise<void> {
  await request<void>("/api/auth/reset-password/complete", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ userId, token, newPassword }),
  });
}

export async function logout(): Promise<void> {
  await requestWithCsrf<void>("/api/auth/logout", "POST");
}

/** Returns null (never throws) for "not signed in" — a 401 here is an expected, ordinary state,
 *  not an error condition the caller needs to catch. */
export async function getMyPermissions(): Promise<MyPermissions | null> {
  try {
    return await request<MyPermissions>("/api/auth/permissions");
  } catch (err) {
    if (err instanceof ApiError && err.status === 401) return null;
    throw err;
  }
}

export async function listUsers(): Promise<AdminUserSummary[]> {
  return request<AdminUserSummary[]>("/api/auth/users");
}

export async function changeRole(userId: string, role: string): Promise<AccountResponse> {
  return requestWithCsrf<AccountResponse>(`/api/auth/${userId}/role`, "PUT", { role });
}

export async function setEnabled(userId: string, enabled: boolean): Promise<void> {
  await requestWithCsrf<void>(`/api/auth/${userId}/enabled`, "PUT", { enabled });
}

export async function issuePasswordReset(userId: string): Promise<{ token: string }> {
  return requestWithCsrf<{ userId: string; token: string }>(`/api/auth/${userId}/reset-password`, "POST");
}

export async function revokeSessions(userId: string): Promise<void> {
  await requestWithCsrf<void>(`/api/auth/${userId}/revoke-sessions`, "POST");
}

export async function setSessionTimeout(userId: string, sessionTimeoutMinutes: number): Promise<void> {
  await requestWithCsrf<void>(`/api/auth/${userId}/session-timeout`, "PUT", { sessionTimeoutMinutes });
}

export async function getPermissionMatrix(): Promise<PermissionMatrixEntry[]> {
  return request<PermissionMatrixEntry[]>("/api/auth/permission-matrix");
}

/** currentPassword is required only when replacing an already-active MFA factor - the server
 *  rejects with current_password_required if it's needed and missing. */
export async function enrollMfa(currentPassword?: string): Promise<MfaEnrollmentResult> {
  return requestWithCsrf<MfaEnrollmentResult>("/api/auth/mfa/enroll", "POST", { currentPassword: currentPassword ?? null });
}

export async function confirmMfa(code: string): Promise<void> {
  await requestWithCsrf<void>("/api/auth/mfa/confirm", "POST", { code });
}
