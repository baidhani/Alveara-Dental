# Test results - ALV-CONTROL-C01 R01 (Stage 1)

**Cadence classification: LOW RISK** (display logic: a label table, CSS classes and test tables on the Command Center; no backend, auth, money, schema, build-configuration or test-infrastructure change). **A focused Stage 1 package: no full backend run was made and none is required before approval** (reviewer direction). After approval the corrected **frontend gate** is rerun once (`stage2/`).

Implementation `69c6f7fe0c4155338b8d752804878d44784348a6`; data commit `60b2576a1d1653cec4db68ca3e3ccc915d3db576` (the control record and catalog entry the new item needs); every result below was run on the committed, clean tree at `60b2576`.

| # | Command | Result |
|---|---|---|
| 1 | `cd src/alveara-client && npx tsc --noEmit` | exit 0 |
| 2 | `npx playwright test` (the whole mocked Playwright suite, desktop and tablet projects) | **140 passed** (136 before + 4 new: two new tests in each of the two projects), 16.8 s |
| 3 | `npx playwright test e2e/architecture-map.spec.ts` | **40 passed** (the Architecture Map spec at desktop and tablet), including the previously failing legend test |
| 4 | `node --test tests/*.test.mjs` | **13 passed** (8 before + 5 new in `tests/status-labels.test.mjs`) |
| 5 | `npm run lint` | exit 0 (the existing warnings are unchanged) |
| 6 | `npx vitest run` | 1,544 passed in 97 files (unchanged; no client source changed) |

Raw outputs: `artifacts/`. Negative controls: `artifacts/negative-controls.txt` (removing the two labels fails 1 repository test and 4 Playwright tests).

## What the new tests assert
- **Repository (`tests/status-labels.test.mjs`, 5):** the schema parser finds the eleven statuses (so it cannot pass on an empty list); the label table has exactly the statuses `.alveara/HANDOFF_SCHEMA.md` section 1 allows; every label is a readable phrase, not the raw status, and no two share a label; every status in the execution ledger is allowed; the Architecture Map and the Engineering stories list both read the one table and the page loads it first.
- **Architecture Map spec (2 new tests, desktop and tablet):** the exhaustive assertion against the authoritative allowed statuses (replacing the closed six-status table); and a rendering test in which the ledger is served with one story in each of the eleven statuses and each shows its label on the card, in the legend (one key per label), in the delivery-state filter (the count matches), and in the story dialog (the label, never `REVALIDATION_REQUIRED`).
- The existing specs (`architecture-map.spec.ts`, `command-center.spec.ts`) no longer carry their own closed status tables; they read the page's one table.

## Frontend gate after approval (frozen)
`stage2/run_frontend_gate.sh <implementation-sha> <out-folder>` on the closure tree (a closure-only commit changes only the review artifact, `EXECUTION_STATUS.json` and `BUILD_STATE.md`), clean tree: `tsc --noEmit`, `vitest run`, `npm run build`, `npm run lint`, `npx playwright test` and `node --test tests/*.test.mjs`; `stage2/evaluate_frontend_gate.py` decides: all exit 0; Vitest 1,544 tests in 97 files; **Playwright 140 passed, 0 failed**; repository checks **13 pass, 0 fail**; the tree clean before and after; no file other than the three closure files differs from the data commit `60b2576`. No backend run. The resubmitted ALV-N016 and ALV-007-C01 packages then need one shared Stage 2 on a tree containing this correction (their evaluators' client counts change to 140 and 13), per the reviewer.

## Disclosures
- The new item has no line in the execution plan's 53 first-release records; the reviewer created it (as `ALV-CONTROL-C01`) in response to finding ALV-N016-R03-01, so the ledger count rises to 57 and the plan's "exactly 53" statement is superseded by the ledger (as it already was by `ALV-N016`).
- The item is recorded as phase `Foundation`, item number 57 (last), `new_production`, depends on `STORY-000`; STORY-000 itself is **not reopened** (the reviewer's decision).
- Three older ALV-010-C01 commits carry a `Co-Authored-By` trailer against the owner's standing instruction; not rewritten. This item's commits have none.
- A first run of my new rendering test failed: it expected "Planned" for a planned course story that the page rightly shows as "In progress" (criteria already passing); the test was corrected to use the page's own rule before the evidence above.
