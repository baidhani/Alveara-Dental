# ALV-008-C02 R02 - Test results

## Risk declaration (Binding Testing Cadence Notice and its review response, 2026-10-13)
**HIGH RISK.** Criteria from the notice: a new migration and a persistent model change (`AddCompletionChartVersion`: a column, a check, trigger 51173, a data backfill); the authoritative completion workflow and its transaction and concurrency behaviour (an extra save, a new in-transaction read); and, from R01, billing integration and parent-test infrastructure. The review response states that an R02 of this story "must follow the high-risk path". The full backend, frontend, repository-check and mocked-browser regression were therefore run on the final tree, together with the story's real-browser walkthrough and the walkthroughs of the changed neighbours. Per the response (Q4) the full 25-run sweep was not repeated; the walkthroughs run are listed below and the reason for the selection is given.

All on the final committed tree (implementation commit `d03b9e7751c9b3221f22fbfc8656725b92e18782`), one machine, 2026-10-13. Raw output is under `artifacts/`.

## Gates, exact commands and results
| Gate | Command | Result (pass / fail / skip) | Where |
|---|---|---|---|
| Backend, full serial | `dotnet build --nologo` then, in `src/Alveara.Api.Tests`, `dotnet test --nologo --no-build --logger "trx;LogFileName=c08c02_r02_full.trx"` (no parallelism, no rebuild during the run) | **3324 passed / 0 failed / 0 skipped** of 3324, 1 h 31 m (R01's final run: 3302) | `artifacts/test-runs/backend-full-summary.txt` |
| Frontend unit | `npx vitest run` | **1392 passed / 0 failed / 0 skipped** of 1392 (91 files; unchanged) | `artifacts/test-runs/frontend-gates.txt` |
| Type check, build, lint | `npx tsc --noEmit`, `npm run build`, `npm run lint` | exit 0 each | same |
| Mocked browser | `npx playwright test` (default config) | **136 passed / 0 failed / 0 skipped** | same |
| Repository checks | `node --test tests/*.test.mjs` (repository root) | **8 passed / 0 failed / 0 skipped** | same |
| Real-backend walkthroughs (fresh database each, `sh run_e2e.sh <db> <config>`) | adoption (the story's own), completion and treatment-plans-complete (the completion workflow changed), billing, billing-ledger and billing-statements (they complete, charge and read completions), odontogram and odontogram-longitudinal (the chart's version history is read) | **8 runs, 82 passed / 0 failed / 0 skipped** | `artifacts/regression/` |
| Targeted subsystem runs while building | `dotnet test --no-build --filter "FullyQualifiedName~..."` for the evidence, reconciliation, adoption, schema, completion and odontogram classes | each passed before the full run | in the full run |
| Leak check | `sqlcmd` counts of `AlveraTest%`, `AlveraRestore%` databases, `alveara_%` logins and databases | unchanged, 54 / 59 / 273 / 248 | `artifacts/test-runs/leak-check.txt` |
| Migration rollback and roll-forward | `CompletionChartEvidenceTests.Rolling_back_and_forward_restores_the_evidence_...` and the backfill-cannot-derive cases, `CompletedProcedureMigrationTests` and `CompletionAdoptionSchemaTests` (they roll back through this migration), inside the full run | passed | the full run |
| Negative controls (proportionate: a new guard and invariant) | `python negctl_r02.py <log>` (12 mutants of the new evidence rules, the matching filter each) | **12 of 12 killed** after two missing tests were added (first pass 10 killed, 2 survivors) | `artifacts/test-runs/negative-controls-r02.txt` |

## The reproduction
`A_later_legitimate_chart_change_at_the_same_practice_clock_instant_leaves_an_adopted_completion_clear` failed on the R01 code (`chartMismatch` 1, expected 0; the test asserts the timestamp tie is real) before any fix, and passes now.

## New and changed backend tests
`CompletionChartEvidenceTests` (22), the reproducing test in `CompletionReconciliationTests` (the old seeded-mismatch test there moved into the new class as the evidence theory), and the two `CompletedProcedureSchemaTests` that now supply valid chart evidence (assertions unchanged).
