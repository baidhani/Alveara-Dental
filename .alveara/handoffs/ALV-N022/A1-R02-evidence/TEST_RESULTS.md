# ALV-N022 A1-R02: test results

Implementation `38f478e`; runs on 2026-11-17 at repository HEAD `37d2b5b` (review closure; no `src/` or `tests/` difference from `38f478e`, see `artifacts/git-state.txt`). Windows 11, local SQL Server, Chromium.

| Run | Result | Duration |
|---|---|---|
| `npx vitest run src/pages/clinical/FinalizeReview.test.tsx src/pages/clinical/ChartSectionPanel.test.tsx src/services/safetyApi.withSafetyNotice.test.ts --reporter=verbose` | 3 files, **15 / 15** | 5 s |
| `Invoke-RealBackendWalkthrough.ps1 -Config playwright.clinical.config.ts` (fresh build, fresh database) | **9 / 9** | 80 s |

New tests (FinalizeReview.test.tsx): counts a chart-backed section's items on the chart; includes entries recorded before the chart became the one record; no count for a confirmed empty chart section while dental history keeps "(2)".

A1-R01 results (232/232 backend, guard 3/3, four walkthroughs, eleven controls) stand: no file they cover changed.
