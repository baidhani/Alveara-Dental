# STORY-010 test results

| Layer | Command | Result |
|---|---|---|
| Backend, full serial | `dotnet build --nologo` then, in `src/Alveara.Api.Tests`, `dotnet test --nologo --no-build --logger "trx;LogFileName=story010_full.trx"` (no parallelism, no rebuild during the run) | **4040 passed / 0 failed / 0 skipped** of 4040, 2 h 18 m (previous full run 3704; +336 from this story) |
| Backend, this story's classes | `DocumentFileRulesTests` 132, `DocumentSchemaTests` 53, `DocumentImportServiceTests` 44, `DocumentLibraryServiceTests` 21, `DocumentSettingsServiceTests` 40, `DocumentApiTests` 37, `DocumentPermissionTests` 10 | 337 cases, all passing |
| Client | `npx vitest run` in `src/alveara-client` | **1503 passed** of 1503 (95 files); `npx tsc -b` clean; `npm run build` succeeds |
| Real backend, browser | `.\scripts\Invoke-RealBackendWalkthrough.ps1 -Config playwright.documents.config.ts` | **14 of 14 passed**; evidence in `docs/testing/documents-walkthrough/` |

Every layer was written test first (each new class was red against a stub or a missing route before the code existed) and checked by mutation: the file rules (6 breakages, 6 caught), the schema triggers (5 of 5), the import service (7, 6 caught and 1 equivalent), the library and edit services (6 of 6), the settings service (7, 6 caught and 1 equivalent covered by the database), the routes (8, 7 caught and the 8th led to an added test) and the screens (8, 7 caught and 1 redundant guard). A flaky simultaneous-import test exposed a real race (a repeat answered with a duplicate warning); it was fixed and the test strengthened.
