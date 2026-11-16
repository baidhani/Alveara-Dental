/**
 * ALV-N021 A3 (briefing 4.1-1, BR-5): the one builder for a fake signed-in `GET /api/auth/permissions` response in tests. It always carries the practice
 * zone (`practiceTimeZoneId`, default America/Chicago) and a session expiry 30 minutes ahead, so a missing zone in a test is a deliberate case
 * (`practiceTimeZoneId: null`), never an accident. Fields passed in override the defaults.
 */
export const TEST_PRACTICE_ZONE = "America/Chicago";

export function signedInSession<T extends Record<string, unknown>>(fields: T): T & { practiceTimeZoneId: string | null; sessionExpiresAtUtc: string | null } {
  return { sessionExpiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(), practiceTimeZoneId: TEST_PRACTICE_ZONE, ...fields };
}
