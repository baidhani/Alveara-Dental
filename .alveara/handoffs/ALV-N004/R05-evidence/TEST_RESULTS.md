# ALV-N004 R05 — Test Results

Implementation commit `2d9819320f02944fc4ab8055a919abf06cd8a8c5`.

## Backend — `dotnet test` (full)
**375 of 375 passing, 0 failed, 0 skipped** (16 m 38 s). Real SQL Server (LocalDB) BACKUP/RESTORE, real files, real OpenPGP, real GnuPG.

| Class | Tests |
|---|---|
| BackupCryptoTests | 22 |
| BackupServiceTests | 20 |
| BackupRestoreTests | 20 |
| BackupSchedulerTests | 9 |
| BackupApiTests | 12 |
| BackupRecoveryHardeningTests | 30 (4 new this attempt) |

## Frontend
`npx vitest run`: **158 of 158**; `tsc --noEmit` clean; `npm run build` clean.

## Real-backend, real-browser
Fresh `AlveraE2E`, real `dotnet run --no-build` API, `npx playwright test --config=playwright.auth.config.ts`: **12 of 12**. Output: `artifacts/playwright-real-backend/run-output.txt`.

## Not run
`npm run lint`; default mocked `npx playwright test` (environment-dependent; see R02 evidence).

## Totals
375 + 158 + 12 = 545.
