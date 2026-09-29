# ALV-N002 R07 — Acceptance Evidence

Updates R06's mapping. R06's topology was correct but its evidence was narrative; R07 re-runs the same real two-VM topology through the new versioned verification harness and commits the raw machine-generated output. See `DEMO_EVIDENCE.md` for the file-by-file mapping.

| # | Acceptance item | R06 review assessment | R07 evidence | R07 status |
|---|---|---|---|---|
| 1 | Local Windows server / LAN client / no public internet | PARTIAL — plausible but not durably reviewable/replayable | `artifacts/client/20260929-000014-routes-during-block.txt` (all 4 routes 200 during the block) + `20260929-000014-isolation-window.txt` (public-internet probe failing in the same window) + `20260929-000014-revert-confirmation.txt` (clean auto-revert) — all machine-generated, timestamped, committed files. | **PASS** |
| 2 | Schema migration initializes and upgrades safely | PASS | Unchanged; reconfirmed via the genuinely rerun automated suite (`TEST_RESULTS.md`). | **PASS** |
| 3 | Time/timezone and money invariants | PASS | Unchanged. | **PASS** |
| 4 | Explicit disconnected-server behavior | PASS | Unchanged. | **PASS** |
| 5 | Persisted job survives restart without duplicate effects | PASS | Unchanged. | **PASS** |
| 6 | Versioned privacy-minimized measurement event | PASS | Unchanged. | **PASS** |
| 7 | LAN client uses API rather than direct database/storage access | PARTIAL — claimed runtime identity, SQL role, real-path ACL, and client denials lacked durable independently verifiable outputs | `artifacts/server/20260928-235600-process-identity.txt` (real process owner via CIM), `20260928-235600-sql-role-membership.txt` (live `sys.database_role_members` query), `20260928-235600-storage-acl.txt` (real `icacls` on the real path), `20260928-235600-ordinary-denial-result.txt` (real denial), `20260928-235600-svc-write-result.txt` (real success) on the server side; `artifacts/client/20260929-000014-bypass-checks.txt` (SQL port unreachable, no file share) on the client side. | **PASS** |
| 8 | Truthful service/database/runner System Status | PASS | Unchanged; also reconfirmed live via the server-side verification run. | **PASS** |
| 9 | Failure paths do not silently corrupt authoritative data | PASS | Unchanged. | **PASS** |

**Total: 9 of 9 PASS**, now backed by committed, machine-generated, timestamped artifact files rather than narrative description.

## Correcting N002-R06-03 (inaccurate R04-decision claim)

`R01`, `R02`, `R03`, and `R05` have independent review decisions stored at `.alveara/reviews/ALV-N002/{R01,R02,R03,R05}.md`. **`R04` remains an unreviewed historical attempt with no decision artifact** — this is stated plainly in `HANDOFF.md`, not glossed over or implied otherwise.

## Correcting N002-R06-04 (snapshot byte-equality)

This attempt's packaging step compares every repository-derived package member against the raw git blob (`git cat-file -p <blob-sha>`, not `git show`, to rule out any working-tree filter/line-ending path) before the ZIP is reported as valid. See `GIT_STATE.md` for the verification method and result.
