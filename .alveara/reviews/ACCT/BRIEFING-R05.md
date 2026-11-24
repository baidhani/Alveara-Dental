# ALV-N030 / ACCT — R05 Setup-Mode Addendum Review

**Decision:** CHANGES_REQUIRED  
**Reviewed material:** section 16 of `docs/design/ACCT_PRE_IMPLEMENTATION_BRIEF.md` (R05 addendum only)

The R04 approval remains valid for sections 1 through 15. The R05 addendum correctly assigns database provisioning, snapshot options, migrations, upgrade backup checkpoints, and Windows service configuration to ALV-N013. It correctly requires detect-and-refuse upgrades, restricted setup mode only on a current database, and a controlled restart after successful Deployment Administrator setup.

## CR-7 — Define client/static behavior and status route across all modes

The mode table says `NotInstalled` and `UpgradeRequired` make **every route** return 503, yet the client is expected to display a useful installation/upgrade message. It also has the setup page poll `GET /api/setup/status` after restart until it receives `setupAvailable: false`, but does not explicitly retain that route in Normal mode.

Revise the contract so that:

- static client assets and the SPA fallback remain available in all modes, while protected/API routes return the stated 503 responses;
- `GET /api/setup/status` is deliberately available in Setup and Normal modes, returning only the minimal boolean state needed by the setup page;
- in NotInstalled and UpgradeRequired it returns the matching safe mode response, without exposing database, connection, migration, or credential detail; and
- the browser proof covers all three rendered states: installation incomplete, upgrade required, and setup restarting-to-normal.

## CR-8 — Make restart request and supervisor handoff testable

`StopApplication()` alone does not intrinsically guarantee a process exit code of 75 or a Windows service restart. Specify the implementation boundary:

- after the successful response has completed, the ACCT host records a restart-request exit code in a process-level mechanism, then requests graceful shutdown;
- the TestHost and `Start-DevApi.ps1` must observe and propagate that exact code; and
- ALV-N013 must configure the Windows service supervisor to restart specifically for that non-zero controlled exit, while treating other exits according to the deployment recovery policy.

Add a focused proof that the successful setup path yields the expected restart-request exit code after the response, while a failed setup remains running and has no restart-request code. The ALV-N013 dependency contract must name the corresponding production-supervisor proof.

No new product decision is needed. Submit R06 with these addendum-only edits; no ACCT implementation is authorized until that review is approved.

