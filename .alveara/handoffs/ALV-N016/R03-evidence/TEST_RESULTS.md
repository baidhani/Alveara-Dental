# Test results - ALV-N016 R03 (Stage 1, combined tree)

**Cadence classification: HIGH RISK** (test infrastructure shared by every database test). **A Stage 1 package: the formal Stage 2 broad run is `PENDING` and has not been run.** A **single shared physical run** will serve this story and `ALV-007-C01` R02 (reviewer direction in `.alveara/reviews/ALV-007-C01/R02-stage1.md`).

## What R03 is
R03 changes **no source**. Its source is the common tree at `37d2b08420d1b24837052aafddb8994bd186d6da` (`37d2b08`): the approved ALV-N016 R02 implementation `0da6f36` (the clone-based rollback design the reviewer approved in `.alveara/reviews/ALV-N016/R02.md`) plus the approved ALV-007-C01 ledger read correction `37d2b08`. The only `src/` difference from the R02 implementation is exactly two files: `BillingService.Ledger.cs` and `BillingLedgerReadConsistencyTests.cs` (checked by command, and enforced by the shared evaluator). The ALV-007-C01 closure-only commit `481dc2d` changed no `src/` file.

## Focused evidence on the combined tree (HEAD `481dc2d`, clean tracked tree, test project rebuilt first)
Filter listed first with `--list-tests`: **318 tests (nonempty)**; `artifacts/focused-r03/filter.txt`.

| What | Result |
|---|---|
| Template tests (`TestDatabaseTemplateTests`, 14), inventory guard (`RealMigrationInventoryTests`, 5), the 13 rollback classes on clones, the 6 real-path classes, `MigrationFailureTests`, and the 5 ledger read-consistency tests | **318 of 318 passed**, 7 m 00 s (`artifacts/focused-r03/tests.csv`) |
| The run's own template (`role: run`) | 1 build, 0 rebuilds, **0 fallbacks**, 255 clones, **32 real-migration fixtures**, build 8.8 s, restore total 83 s (`artifacts/focused-r03/telemetry.jsonl`) |
| Artifact counts before and after (Storage, Keys, Backup, Database, Temp, databases, logins, template backups) | **identical** (only the timestamp header differs; `leak_before.txt` / `leak_after.txt`) |
| Billing, ledger, Gate C/D and procedure-charge classes on the same code (ALV-007-C01 R02, run on clones) | **409 of 409 passed** (`.alveara/handoffs/ALV-007-C01/R02-evidence/artifacts/test-runs/`); this is the corrected dependency running under the template-clone infrastructure |
| Descendant selection (completion, prescriptions, documents) | **1,379 of 1,379 passed** (same package), also on clones |
| Negative controls of the inventory and its guard | unchanged since R02 (`../R02-evidence/artifacts/negative-controls/`): 5 of 5 breakages caught, plus the 13 of R01; no inventory or guard file changed |

## Shared frozen Stage 2 (run once after both Stage 1 closure commits)
`stage2/run_stage2.sh <common-source-sha> <out-folder>` on the common source-equivalent tree (the closure commits change only `.alveara/` files), nothing else running, clean tree; the raw outputs are preserved once in `<out-folder>`, then **two independent evaluators** decide it:

1. `evaluate_stage2.py` (ALV-N016, writes `RESULT_ALV-N016.txt`): run commit is the closure tree; clean before and after; the common source differs from `0da6f36` only by the two approved ledger files; no template mode switch; backend exit 0; **elapsed 3,300 s (55 minutes) or less**; **4,324 passed, 0 failed, 0 skipped**; the 4,300 baseline ids present and Passed, exactly 24 new ids; the 5 ledger tests and `D5_financial_history...` Passed; one template build, 0 rebuilds, 0 fallbacks, no build failure; **at least 3,000 clones; at most 60 real-path fixtures (about 32 expected)**; artifact counts unchanged and no template artifact left; tsc, Vitest (1,544 in 97 files), build, lint, mocked Playwright (136), repository checks (8) pass.
2. `evaluate_stage2_alv007c01.py` (ALV-007-C01, writes `RESULT_ALV-007-C01.txt`): the same run commit and clean-tree checks; backend exit 0 and within a two-hour sanity bound; 4,324 passed with 24 new ids; the 5 ledger tests and D5 passed; zero fallback; counts unchanged; the same client and repository gates.

Both evaluators were smoke-tested against the R01 Stage 2 output: both run and both correctly fail it (count, ledger tests, real-fixture limit), so neither is a rubber stamp. Any shared-run failure is `CHANGES_REQUIRED` for the affected attempt(s); each story then gets its own additive regression package naming the same run commit, the same preserved outputs and the equivalence check.

## Disclosures
- No full run was made for R03. The earlier unplanned full run (ALV-N016 R02 package) found the billing race that ALV-007-C01 R02 corrected; its time (53 m 09 s) was 111 s under the ceiling, a thin margin, and is not Stage 2 evidence.
- Three older ALV-010-C01 commits carry a `Co-Authored-By` trailer against the owner's standing instruction (not rewritten); none of this story's commits has one.
- `ALVERA_TEST_TEMPLATE=off` remains a measurement switch the evaluators require unset.
