# ALV-N021 A3 pre-implementation briefing — R02 review

**Reviewed package:** `Alveara_Briefing_ALV-N021-A3_c5b7c7b_R02.zip`  
**Package SHA-256:** `3EE432F8E75143E6E1A6417227DBFD577E0A27CA02785D37BADA8336EFCD144A`  
**Declared briefing commit:** `c5b7c7b`  
**Date:** 2026-11-16  
**Decision:** **APPROVED — A3 implementation authorized within this briefing.**

## Basis for approval

R02 resolves BR-1 through BR-5:

1. Display formatting and exact `datetime-local` prefill are now separate contracts. The latter uses `formatToParts`, Latin digits, Gregorian calendar, h23 time, and the server-supplied practice zone to produce only the documented ASCII input forms.
2. The five additive local-input routes share one exact invariant-culture parser, with explicit mutual-exclusion, malformed, DST-gap, and DST-overlap outcomes. Legacy UTC fields remain accepted only when supplied alone.
3. The practice zone is correctly treated as a startup/deployment setting, not a live user preference. A permissions refresh can replace the server answer after reconnect/redeployment; no misleading ordinary live-zone-change promise remains.
4. The revised 85-row inventory supplies a disposition for every candidate display, browser-day, local-input, wrapper, caller, date-only, logic, and non-date site. The guard is narrowly aimed at browser-zone instant rendering and browser-local calendar-day derivation, with explicit exceptions.
5. The authenticated session field, shared client session type, auth state, and the listed fake/fixture responses are included in the implementation inventory. The IANA API contract is client-neutral for the future WPF client; any Windows mapping remains server-owned.

## Authorization and limits

Implement only the approved A3 scope in the R02 briefing and its inventory baseline. Report any inventory discrepancy, new API route, schema/migration need, or change to the legacy UTC compatibility policy before implementing it.

Focused development tests reasonably expected to complete within five minutes remain permitted under the standing owner policy. A real-browser two-zone run, if expected to exceed five minutes, requires the owner's explicit permission first. Do not run a broad/full regression; it remains deferred to ALV-N028.

Return a normal A3 Stage 1 handoff at one frozen implementation SHA for independent review before any Stage 2 contract is run.
