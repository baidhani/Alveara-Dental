# ALV-N002 R04 — Acceptance Evidence

Updates R03's mapping with this attempt's corrections. Cross-referenced against the reviewer's own R03 acceptance-assessment table, which is authoritative (not R03's own package, which understated the gap by calling items 1/7 `PARTIAL` rather than the reviewer's stricter `NOT ESTABLISHED`).

| # | Acceptance item | R03 review assessment | R04 evidence | R04 status |
|---|---|---|---|---|
| 1 | Local Windows server / LAN client / no public internet | NOT ESTABLISHED (clean publish omitted the shell; `localhost` resolution is not a LAN hostname trust path; public internet not disabled) | The specific *code* defect is fixed: a clean `dotnet publish` now includes and serves the shell (`/`, `/system-status`, `/api/health` all 200 from a fresh publish output — see `TEST_RESULTS.md`). The *infrastructure* requirements — a real LAN hostname with a certificate a genuinely separate client trusts, public internet disabled while LAN stays reachable — are **not addressed by this attempt**; they need a second machine (a VirtualBox VM is being provisioned by the user) and OS/network configuration outside this sandboxed repository. | **NOT ESTABLISHED** — honestly unchanged; the code defect blocking it is gone, the infrastructure proof still doesn't exist. |
| 2 | Schema migration initializes and upgrades safely | PASS | Unchanged. | **PASS** |
| 3 | Time/timezone and money invariants | PASS | Unchanged. | **PASS** |
| 4 | Explicit disconnected-server behavior | PASS | Unchanged. | **PASS** |
| 5 | Persisted job survives restart without duplicate effects | PASS | Unchanged. | **PASS** |
| 6 | Versioned privacy-minimized measurement event | PASS | Unchanged. | **PASS** |
| 7 | LAN client uses API rather than direct database/storage access | NOT ESTABLISHED (HTTP non-exposure alone insufficient; no deployed identity/ACL check) | Unchanged — no OS-level service identity or filesystem ACL was configured or tested this attempt. Same infrastructure dependency as item 1. | **NOT ESTABLISHED** |
| 8 | Truthful service/database/runner System Status | PASS | Unchanged. | **PASS** |
| 9 | Failure paths do not silently corrupt authoritative data | PASS for exercised paths, but the separate logging/privacy defect (finding 2) still blocked the story's security requirements | The logging/privacy defect is fixed: `appsettings.json`/`appsettings.Development.json` suppress EF's `Database.Command`/`Update` and `AspNetCore.Diagnostics` logging categories; `SafeExceptionHandlingMiddleware` logs only exception type + correlation id for any unhandled request exception. `FrameworkLoggingPhiSafetyTests` proves a synthetic patient-like value never reaches any captured log record across any category during a real duplicate-key violation, through the real fully-configured pipeline. | **PASS**, defect resolved. |

**Total: 7 of 9 PASS, 2 of 9 NOT ESTABLISHED (items 1 and 7).** `EXECUTION_STATUS.json`'s `acceptance` block and `blockingIssues` for this attempt reflect this — items 1 and 7 are listed as open blocking issues, not silently reported as an empty blocker list against a partial pass count (the exact inconsistency the R03 review flagged).

## Why this is reported `AWAITING_REVIEW` rather than `BLOCKED`

The story's other seven acceptance items, and the three specific R03 findings that were correctable in this sandboxed environment, are genuinely resolved and independently verifiable right now. Per `HANDOFF_SCHEMA.md`, `CHANGES_REQUIRED` preserves an attempt's history and continues the *same* story at the next attempt; a `BLOCKED` status is for when *no further correction is possible* without the missing input. Items 1 and 7 are not fully blocked — infrastructure provisioning is actively underway (see `NEXT_STORY_IMPACT.md`) — so this attempt reports its real, partial acceptance state honestly (7/9, 2 explicitly open) rather than either overclaiming completion or halting all further ALV-N002 work while the VM/cert/ACL setup is in progress.

## Required tests (from the story prompt) — re-mapped

| Required test | R03 gap | R04 evidence |
|---|---|---|
| Deployable production-shell test | Clean `dotnet publish` output omitted the client | Clean-publish reproduction in `TEST_RESULTS.md`; `Alveara.Api.csproj`'s publish-time client build/copy targets |
| Framework-level PHI-safe logging test | Only the application's own logger was tested; EF/diagnostics categories leaked | `FrameworkLoggingPhiSafetyTests` — real host, real logging pipeline, all categories captured |
