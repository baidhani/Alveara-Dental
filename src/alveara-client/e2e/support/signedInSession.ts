/** ALV-N021 A3 (BR-5): the mocked-browser specs' builder for a signed-in `GET /api/auth/permissions` body; same contract as src/test/signedInSession.ts. */
export const TEST_PRACTICE_ZONE = "America/Chicago";

export function signedInSession<T extends Record<string, unknown>>(fields: T): T & { practiceTimeZoneId: string | null; sessionExpiresAtUtc: string | null } {
  return { sessionExpiresAtUtc: new Date(Date.now() + 8 * 3600 * 1000).toISOString(), practiceTimeZoneId: TEST_PRACTICE_ZONE, ...fields };
}
