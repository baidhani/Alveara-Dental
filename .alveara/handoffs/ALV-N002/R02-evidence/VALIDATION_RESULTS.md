# ALV-N002 R02 — Validation Results

| # | Check | Method | Result |
|---|---|---|---|
| 1 | N002-R01-01 fixed: atomic claim, no duplicate effects | `Two_concurrent_attempts_to_claim_the_same_job_result_in_exactly_one_execution`, `A_handler_s_effect_and_the_job_s_completion_commit_atomically_in_one_transaction`, `Recovery_does_not_re_invoke_the_handler_when_an_effect_receipt_already_exists_for_the_job` | **PASS** |
| 2 | N002-R01-02 fixed: recovery on every poll, respects MaxAttempts | `Hosted_service_style_recovery_runs_on_every_poll_not_only_once_at_startup`, `Recovery_marks_a_job_that_already_exhausted_MaxAttempts_as_Failed_instead_of_looping_forever` | **PASS** |
| 3 | N002-R01-03 fixed: host survives DB outage at startup, sustained truthful status | `RealProcessDatabaseOutageTests` (real separate OS process) + manual reproduction (see `TEST_RESULTS.md`) | **PASS** |
| 4 | N002-R01-03 fixed: client hook has timeout/overlap-guard/shape-validation/stale-state | `useSystemStatus.test.ts` (5 new tests) | **PASS** |
| 5 | N002-R01-04 fixed: nested/array/oversized/out-of-range values rejected | `MeasurementEventTests.cs` (11 new tests reproducing the exact reviewer bypasses) | **PASS** |
| 6 | N002-R01-05: real (non-loopback) LAN HTTP+HTTPS reachability | Manual reproduction against `192.168.1.180` with genuine TLS handshake (see `TEST_RESULTS.md`) | **PASS**, with honestly documented scope limits (see `R02.md`) |
| 7 | N002-R01-05: least-privilege database access actually enforced | `LeastPrivilegeAccessTests.cs` (2 tests: CRUD succeeds, DDL/BACKUP rejected) | **PASS** |
| 8 | N002-R01-06 fixed: real migration upgrade preserves data | `MigrationUpgradeTests` | **PASS** |
| 9 | N002-R01-06 fixed: failing migration doesn't half-apply or destroy data | `MigrationFailureTests` | **PASS** |
| 10 | N002-R01-06 fixed: interrupted blob write leaves no partial/orphaned file | `An_interrupted_write_leaves_no_partial_or_orphaned_temp_file_behind` | **PASS** |
| 11 | `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, `GPT Docs/` untouched | `git diff --stat` across this attempt | **PASS** — zero diff |
| 12 | Full test suite stable, no flakiness | 4 consecutive full runs of `dotnet test Alveara.slnx`, zero failures | **PASS** |
| 13 | R01 attempt/evidence/ZIP preserved unchanged | `git log --follow` shows no modification to `.alveara/handoffs/ALV-N002/R01*` paths since their original commits | **PASS** |
| 14 | JSON validity | `node -e "JSON.parse(...)"` on `EXECUTION_STATUS.json` | **PASS** |

**Overall: 14/14 PASS.**
