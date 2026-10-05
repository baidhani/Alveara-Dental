# Acceptance evidence - ALV-012-C01 R01 (6 of 6)

| # | Acceptance item | Evidence |
|---|---|---|
| 1 | Original STORY-012 tests still pass | The four backend classes (`PerioRulesTests`, `PerioSchemaTests`, `PerioServiceTests`, `PerioApiTests`) and the frontend `Perio.test.tsx` pass in the full runs with **no test file edited**; the **original STORY-012 real-backend walkthrough passed unchanged** on the final tree (`artifacts/parent-regression-walkthrough/`). See `PARENT_REGRESSION.md`. |
| 2 | All six periodontal sites are distinguishable per tooth | `PerioSiteModelTests` (the reference sites, six different readings on each of the 32 teeth, the site codes), `perioSiteModel.test.ts` (the same table in TypeScript), the SHA-256 of the whole sweep asserted in both languages, and the real-browser step `KEYBOARD FLOW` (six distinct names on a back tooth). |
| 3 | CAL, mobility, furcation, suppuration and plaque can be recorded | CAL: `PerioChartValidatorTests` (the rule at its boundaries, no field to store it), shown on every reading (`PerioSessionServiceTests`, `PerioSessionApiTests`, the comparison tables). Mobility, furcation, suppuration and plaque: `PerioSessionSchemaTests` (database ranges and rules), `PerioSessionServiceTests.A_tooth_is_saved_with_its_six_sites_and_grades...`, `PerioSessionApiTests`, `PerioSteps.test.tsx` (quick keys, tooth controls, not assessed vs none), real-browser `MEASURES AND TEETH`. |
| 4 | Full-mouth sequential entry works without losing site context | `PerioSteps.test.tsx` "a whole mouth - all 192 sites - is typed without the mouse" (checks the position after 24 sites and the finished chart), the real-browser step `A WHOLE MOUTH WITHOUT THE MOUSE` (192 sites typed from the keyboard, each exactly once, first and last digits at the first and last sites), the sweep tests (every site once, never more than one tooth apart, excluded teeth skipped without disturbing the order), resume, go-back and jump tests. |
| 5 | Prior finalized chart can be compared with the current chart | `PerioComparisonTests` (hand-worked fixture: counts, means, trend boundaries at 1 and 2 mm, unmatched sites counted separately, empty and identical charts), `PerioComparisonServiceTests` (which charts, other patients refused), `PerioSessionApiTests`, `PerioCompare.test.tsx` (the same fixture on screen, in words), real-browser `COMPARISON AND LINKS` (a saved chart, the draft live and the API agree). |
| 6 | Invalid values are rejected without discarding unrelated valid entry state | `PerioSessionServiceTests.A_wrong_entry_refuses_the_whole_save...` (the draft and its version exactly as they were; the valid tooth still there), `PerioSessionApiTests`, `PerioSteps.test.tsx` (a refused save keeps the entries and marks the problem), real-browser `A WRONG ENTRY IS REFUSED WITHOUT LOSING WHAT WAS TYPED` (a real server refusal; entries kept; X is the way out). |

## Required tests (from the prompt)
- Original regression - item 1.
- Six-site mapping tests - item 2.
- Measurement/CAL validation tests - `PerioChartValidatorTests`, `PerioSessionSchemaTests`.
- Full-mouth keyboard-flow test - `PerioSteps.test.tsx` and the real-browser whole-mouth step.
- Comparison test - item 5.
- Session rollback/concurrency test - `PerioSessionServiceTests` (a failed audit write and a failed insert halfway through a finalize leave no half-chart and the draft open; a failed audit write on a save applies none of the batch; a stale edit; six simultaneous saves and finalizes), `PerioSessionApiTests` (two people at the same moment: one 200, one 409), real-browser `STALE DRAFT`.

## Failure paths required by the prompt
Out-of-range values (service, API, database, screen); invalid site or tooth state (excluded or missing teeth, a furcation on a single-rooted tooth, grades on an excluded tooth); partial save failure; stale session edit; a missing tooth met during sequential entry (skipped and listed; a reading refused with the way out; a tooth that becomes missing mid-entry refused by the server without losing what was typed).
