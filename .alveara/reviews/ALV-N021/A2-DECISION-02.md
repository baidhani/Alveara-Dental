# ALV-N021 decision 02 — time-zone and culture hardening

**Date:** 2026-11-14  
**Reviewed package:** `Alveara_DecisionRequest_ALV-N021_02_674a04e.zip`  
**Package SHA-256:** `8120FADB7EECA4CBA0240A55D14EE3E3CEE530D96C81613640881B3A7BD26278`  
**Declared request commit:** `674a04e`  
**Decision:** **APPROVED WITH SPLIT PLACEMENT — T-2 and T-3 are authorized in A2; T-1 and T-4 are authorized as new A3 immediately after A2, subject to an approved A3 pre-implementation briefing.**

## Evidence and rationale

The request's audit is consistent with the actual repository. `IPracticeClock` already gives server-side practice-local conversion and explicit invalid/ambiguous-local-time handling. However, `ReportFilterResolver` owns a duplicate `StartOfDayUtc` implementation, while reports, calendar and board need one shared rule. The client also has browser-local instant formatting, browser-local “today” calculation, and `datetime-local` flows whose plain local values can be interpreted as browser time instead of practice time. These are genuine consistency risks for remote access or a practice outside the browser's zone.

The approximately 21 server formatting/parsing sites are a separate correctness risk: a date/time string that is a wire value, cache/key value, structured record, or machine-readable text must not depend on the Windows process culture. The reported Thai Buddhist-calendar and nonstandard-time-separator cases are valid negative scenarios.

The Execution Plan has been amended to record the authorized A1/A2/A3 sequence and the A3 acceptance/coverage boundary. No code is authorized by this decision alone.

## Authorized A2 scope — T-2 and T-3 only

### T-2 — explicit culture at every machine boundary

1. Add `CultureInfo.InvariantCulture` explicitly to every date/time `ToString`, `TryParseExact`, and `ParseExact` that produces or consumes a wire value, key, persistence-compatible textual value, machine-readable log/evidence value, or fixed-format UI/API contract. Do not change intentional human-localized display text without separately defining that display contract.
2. Do **not** set `DefaultThreadCurrentCulture` or `DefaultThreadCurrentUICulture` to invariant as a second guard. That global change can alter unrelated human-facing/framework behavior and conceal a missed boundary. The source guard and focused tests must prove each required call is explicit.
3. Create a narrow, reviewed source-scan guard for the identified production paths. Its allowlist must distinguish date-only values and intentional localized display from machine boundaries; it must not merely suppress matches globally.
4. Test under a non-Gregorian/non-English culture and a culture with a nonstandard time separator, plus ordinary invariant/Chicago behavior. Include a negative control showing that omission of the explicit provider is caught by the guard or changes the tested fixed-format result.

### T-3 — one practice-day boundary rule

1. Move the practice-local-day-to-UTC-boundary rule to `IPracticeClock` (or an equally central clock abstraction), and migrate `ReportFilterResolver`, report drill paths, calendar, and visit-board consumers to it. Remove the duplicate application implementation rather than allowing parallel rules.
2. The rule takes a `DateOnly` practice date. At an invalid local midnight it returns the first valid instant of that local day; at an ambiguous midnight it chooses the earlier instant. Its behavior and terminology must be documented once at the shared boundary.
3. Cover a time zone with a midnight transition, a normal Chicago day, inclusive report-range/end-exclusive behavior, Calendar/Flow Board requests that previously could receive 400, and a negative control proving a direct/old boundary implementation is not silently retained.
4. Preserve date-only semantics: birth dates, service dates, and other `DateOnly` values are not instants and must not acquire an accidental zone conversion.

## Authorized follow-on A3 scope — T-1 and T-4

A3 is deliberately after A2. It prevents the large presentation/input inventory from delaying the atomic visit-transition and checkout release, while keeping the time-zone remediation within ALV-N021 before ALV-N022. Before any A3 code or tests are written, submit an A3 pre-implementation briefing for independent approval. It must enumerate every affected site and make the following contract concrete.

1. **Practice-zone display:** all user-visible instants, including financial-document views and audit/history timestamps, use one shared client formatter configured with the server-provided practice `timeZoneId`. The formatter uses `Intl.DateTimeFormat` with that zone; it must never silently fall back to the browser zone. If the configuration is unavailable or invalid, show a clear unavailable state and do not render a misleading local time.
2. **Practice “today”:** filters, relative labels, calendar/board navigation defaults, and aging-adjacent live views derive the local date in the configured practice zone. No direct browser-clock calendar-day calculation remains outside reviewed test helpers.
3. **Plain local input:** a `datetime-local` value is a plain practice-local text value, not a browser instant. Add the narrowly named `...Local` request fields where needed; parse them exactly with invariant culture on the server and convert only through `IPracticeClock.FromPracticeLocal`. Preserve a documented legacy UTC request contract only where compatibility actually requires it. A payload containing both local and UTC forms is refused as ambiguous. Invalid and ambiguous DST local input returns a field-level validation result for the local field.
4. **Statement/display consistency:** financial documents, live billing/aging, and all other visible instants follow the same practice-zone display policy. This does not alter the A1 versioned immutable-document/reprint contract: issued document canonical content still uses its stored/versioned basis.
5. **Guardrails and proof:** include a narrow scan/negative-control policy for direct instant formatting, real browser tests from different browser zones against one practice zone, a zone whose practice date differs from the browser date, DST gap/overlap input refusal, legacy-only request compatibility, and proof that date-only fields are unchanged. Existing behavior in the configured Chicago practice zone must remain correct.

## Execution and review order

1. Complete and close A2 under its already approved workflow/checkout briefing, incorporating only T-2 and T-3 above.
2. Submit the A3 pre-implementation briefing before A3 implementation. It must state the complete inventory, API compatibility map, migration/database impact (if any), source-scan allowlist, focused test list, and the no-silent-browser-fallback behavior.
3. A3 receives normal two-stage review. The standing focused-test permission (expected duration of five minutes or less) remains available; any longer/unknown, walkthrough, broad, or full run needs the owner's explicit permission. No broad/full regression is requested here; it remains deferred to ALV-N028.

Retain this decision and the immutable request package with the ALV-N021 review record. A1's completed approval remains unchanged.
