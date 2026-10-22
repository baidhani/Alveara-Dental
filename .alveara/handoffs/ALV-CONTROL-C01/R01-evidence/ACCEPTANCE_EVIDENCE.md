# Acceptance evidence - ALV-CONTROL-C01 R01 (5 of 6 at Stage 1; item 6 is the post-approval frontend gate)

The reviewer's direction (conversation with the reviewer, 2026-10-22, answering finding ALV-N016-R03-01 and `.alveara/reviews/ALV-N016/R03.md` / `.alveara/reviews/ALV-007-C01/R02.md`) is the authority for this item; it is the acceptance list.

| # | Criterion | Evidence |
|---|---|---|
| 1 | Human-readable labels for every status allowed by `.alveara/HANDOFF_SCHEMA.md` (including READY, IN_PROGRESS, AWAITING_REGRESSION, DEFERRED, REVALIDATION_REQUIRED) | `assets/status-labels.js` (eleven labels); `tests/status-labels.test.mjs` (table equals the schema's allowed set; readable; distinct) |
| 2 | Legend, cards, filters and popup details updated consistently; filter and keyboard behaviour preserved | `assets/map.js` and `assets/app.js` read the one table; styles for the new states; the new rendering test checks card, legend, state filter and dialog for all eleven statuses; the existing legend, filter, keyboard-focus and dialog tests pass unchanged in assertion |
| 3 | The test's closed status list replaced by an exhaustive assertion against the authoritative allowed statuses | `e2e/executionStatus.ts` (parses the schema; reads the page's table); both specs use it; the exhaustive test "labels exactly the statuses HANDOFF_SCHEMA.md allows ..." |
| 4 | Focused Playwright at desktop and tablet plus the relevant repository tests | 140 of 140 (whole mocked suite), 40 of 40 (Architecture Map spec), repository 13 of 13; tsc, lint, Vitest clean |
| 5 | STORY-000 not reopened and backend/test infrastructure untouched; no full backend run before approval | `CHANGED_FILES.md` (no file under `src/Alveara.Api*`, no migration); STORY-000's coexistence test passes (in the repository checks) |
| 6 | After Stage 1 approval the corrected frontend gate is rerun | **PENDING** (`stage2/`, frontend only) |
