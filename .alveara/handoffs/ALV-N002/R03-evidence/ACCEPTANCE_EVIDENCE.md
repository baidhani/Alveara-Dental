# ALV-N002 R03 — Acceptance Evidence

Updates R02's mapping with this attempt's corrections. Cross-referenced against the reviewer's own R02 acceptance-assessment table, which is the authoritative starting point for this attempt (not the R02 package's own — overclaimed — manifest).

| # | Acceptance item | R02 review assessment | R03 evidence | R03 status |
|---|---|---|---|---|
| 1 | Local Windows server / LAN client / no public internet | NOT ESTABLISHED | `Program.cs` now serves the built client from the same Kestrel process with SPA fallback; verified with a genuine (non-bypassed) TLS validation over the real LAN interface via `curl --resolve` (not `curl -k`); blob-storage isolation from the web-servable path now proven by `StorageIsolationTests`. A genuinely separate physical LAN client machine, a real production Windows Service account, and disabling public internet on this machine were **not** exercised — that requires infrastructure/system-level changes outside this story's sandboxed scope and explicit user authorization. | **PARTIAL** — application-level topology established; full separate-device/offline demonstration remains open, honestly reported below rather than in the "not established" state R02 flagged, since the specific technical objections in that finding (no static serving, `curl -k`) are now resolved. |
| 2 | Migration initializes and upgrades safely | PARTIAL (halfway-rollback unproved) | `FailureTestBrokenMigration.Up()` now performs a real, observable `ALTER TABLE ... ADD COLUMN` before its failing statement; the test explicitly queries `INFORMATION_SCHEMA.COLUMNS` afterward and asserts the added column does not survive — proving a partially-executed migration is genuinely rolled back, not just an immediately-failing one. | **PASS** |
| 3 | Time/timezone and money invariants | PASS (unchanged) | Unchanged. | **PASS** |
| 4 | Explicit disconnected-server behavior | PASS (unchanged) | Unchanged. | **PASS** |
| 5 | Persisted job survives restart without duplicate effects | PARTIAL (enqueue race) | `EnqueueAsync` now handles a genuine SQL unique-constraint violation by re-querying the row the other caller committed instead of racing a check-then-insert; `Two_concurrent_enqueue_calls_for_the_same_idempotency_key_both_succeed_and_return_the_same_job` reproduces the reviewer's exact synchronized-caller scenario and confirms both calls return the same job with exactly one row persisted. Unsafe check-marker-then-act guidance removed from `IBackgroundJobHandler`'s doc comment. | **PASS** |
| 6 | Versioned privacy-minimized measurement event | PASS (unchanged) | Unchanged. | **PASS** |
| 7 | LAN client uses API rather than direct database/storage access | NOT ESTABLISHED as deployed client/permission evidence | Least-privilege SQL access unchanged (already proven in R02); blob-storage web-isolation now proven (`StorageIsolationTests`: a stored blob's bytes never appear in any HTTP response, even the SPA fallback's 200). Same separate-device limitation as item 1. | **PARTIAL** — same reasoning as item 1. |
| 8 | Truthful service/database/runner System Status | PASS (unchanged) | Unchanged. | **PASS** |
| 9 | Failure paths do not silently corrupt authoritative data | PARTIAL (raw sensitive text persisted; migration rollback unproved) | Both underlying causes fixed: failure handling now persists/logs only `{ExceptionTypeName} ({correlation token})`, never `ex.Message`; migration-rollback now genuinely proven (item 2). `A_handler_exception_containing_synthetic_patient_like_text_never_reaches_persisted_LastError` and `The_configured_logging_sink_never_receives_the_raw_exception_message_only_the_safe_summary` directly reproduce and disprove the reviewer's synthetic-patient-text harness result. | **PASS** |

**Total: 7 of 9 PASS, 2 of 9 PARTIAL, 0 of 9 NOT ESTABLISHED / FAIL.** This is an honest correction of the R02 package's manifest, which claimed 9/9 with no blockers while the reviewer found — and this attempt confirms — that items 1 and 7 were not actually established. `EXECUTION_STATUS.json`'s `acceptance` block for this attempt reflects `passed: 7`, `partial: 2`, not `9/9`.

## Required tests (from the story prompt) — re-mapped

| Required test | R02 gap | R03 evidence |
|---|---|---|
| Durable background-job restart/idempotency test | Enqueue race under concurrent callers with the same idempotency key | `Two_concurrent_enqueue_calls_for_the_same_idempotency_key_both_succeed_and_return_the_same_job` |
| PHI-safe diagnostic logging test | Raw exception message persisted/logged | `A_handler_exception_containing_synthetic_patient_like_text_never_reaches_persisted_LastError`, `The_configured_logging_sink_never_receives_the_raw_exception_message_only_the_safe_summary` |
| Migration-failure rollback test | Only an immediately-failing single-statement migration was tested | `A_partially_executed_failing_migration_is_fully_rolled_back_not_left_half_applied_and_does_not_destroy_existing_data` |
| LAN production-shell / storage-isolation test | No static shell serving; no storage-permission check | `StorageIsolationTests` (2 tests) + manual `curl --resolve` reproduction in `TEST_RESULTS.md` |
