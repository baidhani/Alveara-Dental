import { request, requestWithCsrf, fetchCsrfToken } from "./authApi";

/**
 * ALV-N030 (brief sections 4 and 9): first-run setup, the self-service password change, "Add staff member", the legacy-login action and the Practice Owner.
 * Every refusal arrives as an ApiError with the server's stable error code; screens map codes to their own wording.
 */

/** GET /api/setup/status (brief 16.2a): the server's mode. 200 {mode:"setup"|"normal", setupAvailable}; 503 {mode:"installation_incomplete"|"upgrade_required"}. */
export const getSetupStatus = () => request<{ mode?: string; setupAvailable: boolean }>("/api/setup/status", { cache: "no-store" });

/** POST /api/setup/deployment-administrator: the token goes in the JSON body only, never a URL. Every token failure is the same 400 "setup_failed". */
export const completeSetup = (token: string, username: string, password: string) =>
  request<{ id: string; username: string }>("/api/setup/deployment-administrator", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ token, username, password }),
  });

/** POST /api/auth/password/change: the signed-in person chooses their own password; the session is re-issued. */
export const changeOwnPassword = (currentPassword: string, newPassword: string) =>
  requestWithCsrf<void>("/api/auth/password/change", "POST", { currentPassword, newPassword });

export const WORKING_ROLES = [
  { value: "Dentist", label: "Dentist" },
  { value: "Hygienist", label: "Hygienist" },
  { value: "Assistant", label: "Assistant" },
  { value: "FrontDesk", label: "Front desk" },
  { value: "Billing", label: "Billing" },
  { value: "OfficeManager", label: "Office manager" },
  { value: "Admin", label: "Administrator" },
] as const;

/** The 201 answer of "Add staff member" or the legacy-login action: the temporary password is shown ONCE. A replay answers status "already_created" with no password. */
export interface StaffMemberCreated {
  status?: "already_created";
  staffProfileId: string;
  userAccountId: string;
  username: string;
  role: string;
  /** ALV-N030-C01 (brief 18.4): true when this call also designated the person the Practice Owner (or, on a replay, when they are the owner). */
  practiceOwner?: boolean;
  temporaryPassword?: string;
}

async function postWithKey<T>(url: string, body: unknown, idempotencyKey: string): Promise<T> {
  const csrf = await fetchCsrfToken();
  return request<T>(url, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-CSRF-Token": csrf, "Idempotency-Key": idempotencyKey },
    body: JSON.stringify(body),
  });
}

/**
 * POST /api/config/staff-members: profile and login together. The key is derived from the username, so a retry of the same addition is a replay, not a second person.
 * ALV-N030-C01 (brief 18.4): with practiceOwner the same server transaction also designates the person the Practice Owner - both or neither.
 */
export const addStaffMember = (displayName: string, jobTitle: string, username: string, role: string, practiceOwner = false) =>
  postWithKey<StaffMemberCreated>("/api/config/staff-members", { displayName, jobTitle, username, role, practiceOwner }, `staff-member:${username.trim().toLowerCase()}`);

/** POST /api/config/staff/{id}/legacy-login: a new login for an active staff record created before logins were required. */
export const createLegacyLogin = (staffProfileId: string, username: string, role: string) =>
  postWithKey<StaffMemberCreated>(`/api/config/staff/${staffProfileId}/legacy-login`, { username, role }, `legacy-login:${staffProfileId}:${username.trim().toLowerCase()}`);

export interface PracticeOwner {
  staffProfileId: string;
  displayName: string;
  designatedAtUtc: string;
  method: string;
}

export const getPracticeOwner = () => request<{ owner: PracticeOwner | null }>("/api/practice-owner");

/** POST /api/practice-owner/initial: only while no owner has ever been designated (409 owner_already_designated afterwards). */
export const designatePracticeOwner = (staffProfileId: string) =>
  requestWithCsrf<PracticeOwner>("/api/practice-owner/initial", "POST", { staffProfileId, confirm: true });
