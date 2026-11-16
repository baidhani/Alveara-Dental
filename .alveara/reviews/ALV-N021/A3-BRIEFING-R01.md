# ALV-N021 A3 pre-implementation briefing — R01 review

**Reviewed package:** `Alveara_Briefing_ALV-N021-A3_a9f19b2_R01.zip`  
**Package SHA-256:** `EE9E12C245D19ECA11D46E85B0BA5BA170C74BCBC3BE181B557E17C58047205C`  
**Declared briefing commit:** `a9f19b2`  
**Date:** 2026-11-16  
**Decision:** **CHANGES_REQUIRED — return a revised briefing only. Do not write A3 code or tests yet.**

## What is approved in principle

The selected architecture is correct: publish one canonical IANA `practiceTimeZoneId` in the authenticated session response, preserve UTC API instants, and use a shared client presentation helper with that server-authoritative zone. This is compatible with the later WPF client: IANA is the API contract, and any Windows mapping remains server-owned. The rejection of the global-offset serializer is also correct; parsing an offset timestamp into JavaScript `Date` would still return rendering to the browser zone unless the client applied an explicit rule.

The legacy-input decision is also approved in principle: the new product UI sends only `...Local`; a legacy `...Utc` field stays accepted when supplied alone; a request containing both is refused. The plan correctly keeps broad regression deferred to ALV-N028 and requests explicit owner permission for a two-zone real-browser run if it will exceed five minutes.

## Required briefing corrections

### BR-1 — distinguish display formatting from exact local-input formatting

`formatInstant` may use the user's locale for readable display, but it must never supply a `datetime-local` value. Define a separate `toPracticeLocalInput` contract:

- Output is exactly ASCII `yyyy-MM-ddTHH:mm` (and `:ss` only where that field explicitly supports seconds), assembled from `Intl.DateTimeFormat.formatToParts` using the server-provided zone and Latin digits; it must not depend on browser locale, calendar, or browser zone.
- It returns `null` for a missing/invalid zone, as do other practice-time helpers. A form must show an unavailable/error state rather than silently substituting browser time.
- It is presentation only. User-edited input remains plain text and is never passed through `new Date(...).toISOString()`.

### BR-2 — make the server local-input parser exact and unambiguous

Replace the generic `DateTime.ParseExact` wording with a concrete reusable parser contract. It accepts only the documented invariant ASCII patterns, creates a `DateTimeKind.Unspecified` wall-clock value, rejects offsets/`Z` suffixes and all other formats, then calls `IPracticeClock.FromPracticeLocal`. State the stable field-level validation codes and field name for malformed, gap, overlap, and both-fields cases. Use the same parser and proof matrix for all five changed routes.

### BR-3 — correct the zone-change lifecycle claim

`PracticeTimeZone` is a deployment setting used to construct the singleton practice clock at application startup; it is not a live in-app preference. Do not promise a normal live “practice time zone changed” notice as though an administrator can change it during an active session.

The revised briefing should instead specify:

- a deployment-zone change requires restart/redeployment and its normal deployment/backup compatibility controls;
- every authenticated permissions refresh replaces the stored session zone, so a reconnect after deployment uses the current server answer;
- if a rolling deployment or inconsistent server response presents a different valid zone, re-render from the newest authenticated response and show a neutral session-refresh notice only if it is genuinely observable;
- missing/invalid zone still has the no-fallback unavailable state.

### BR-4 — complete the pre-implementation inventory before code starts

Appendix A cannot defer confirmation of class A/B sites until implementation. Before code starts, give every candidate site one explicit disposition: migrate to the shared instant helper; migrate to the practice-day helper; retain as an already-correct shared helper; retain as date-only/instant logic; or exclude with a specific reviewed reason. Include wrapper/helper call sites, not only raw formatting patterns. The reviewer must be able to compare the final changed/allowlisted inventory to this approved baseline.

The source guard must target browser-zone formatting of instants and browser-local calendar-day derivation specifically. It must not accidentally prohibit number formatting, date-only rendering, timers/idempotency keys, or the approved shared helper itself. Its allowlist needs path, expression/category, and rationale—not a broad project-wide exemption.

### BR-5 — bind the session contract to all signed-in roles and test doubles

State that `practiceTimeZoneId` is an additive field on every successful authenticated `GET /api/auth/permissions` response, with no permission check beyond authentication. The implementation inventory must include the shared `MyPermissions`/auth state type and every affected fixture/fake/session response, rather than relying on scattered tests to discover missing fields.

## Required R02 submission

Return only revised design documents and a compact change map addressing BR-1 through BR-5. No implementation, migrations, test execution, or broad regression is authorized for R02. Once the revised briefing is approved, A3 implementation may begin under the normal two-stage process.
