import type { APIRequestContext, APIResponse } from "@playwright/test";

/**
 * ALV-N030 (brief section 17.4, review BRIEFING-R07): browser and chain FIXTURES create accounts through the test-only routes of
 * Alveara.Api.TestSupport. Those routes exist only on ChainHost started by the runners (ASPNETCORE_ENVIRONMENT=Test,
 * TestSupport__Enabled=true, a 127.0.0.1 listener); the product has no sign-up and no first-admin bootstrap any more.
 * These helpers prepare test data only. Real staff creation is the product's "Add staff member" (POST /api/config/staff-members).
 */

export type FixtureRole = "Admin" | "Dentist" | "Hygienist" | "Assistant" | "FrontDesk" | "Billing" | "OfficeManager" | "Unassigned";

export interface FixtureAccount {
  username: string;
  password: string;
  role: FixtureRole | string;
  kind?: "Staff" | "DeploymentAdministrator";
  withStaffProfile?: boolean;
  mustChangePassword?: boolean;
}

/** POST /api/test-support/accounts: an enabled account (201 with { id, username, role, accountKind, staffProfileId, mustChangePassword }). */
export function createAccount(request: APIRequestContext, account: FixtureAccount): Promise<APIResponse> {
  return request.post("/api/test-support/accounts", { data: account });
}

export interface FixtureStaffProfile {
  displayName: string;
  jobTitle?: string | null;
  /** The login the test already made; the profile is linked to it in the same save. */
  userAccountId?: string | null;
  /** Required (true) when there is no account: an unlinked profile is only a legacy/remediation fixture. */
  legacyUnlinked?: boolean;
}

/** POST /api/test-support/staff-profiles: a staff profile linked to an existing login (201 with { id, displayName, jobTitle, userAccountId, isActive, rowVersion }). */
export function createStaffProfile(request: APIRequestContext, profile: FixtureStaffProfile): Promise<APIResponse> {
  return request.post("/api/test-support/staff-profiles", { data: profile });
}
