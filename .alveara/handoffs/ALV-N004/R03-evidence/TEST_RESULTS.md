# ALV-N004 R03 — Test Results

Implementation commit `4b99e2a3e0761e4272b5208f5ff61edbfd0c588c`.

## Backend — `dotnet test` (full)
**366 of 366 passing, 0 failed, 0 skipped** (15 m 38 s). Real SQL Server (LocalDB) BACKUP/RESTORE, real files, real OpenPGP, real GnuPG.

| Class | Tests |
|---|---|
| BackupCryptoTests | 22 |
| BackupServiceTests | 20 |
| BackupRestoreTests | 20 |
| BackupSchedulerTests | 9 |
| BackupApiTests | 12 |
| BackupRecoveryHardeningTests | 21 (7 new this attempt) |

## Frontend
`npx vitest run`: **158 of 158**; `tsc --noEmit` clean.

## Real-backend, real-browser
Fresh `AlveraE2E`, real `dotnet run --no-build` API, `npx playwright test --config=playwright.auth.config.ts`: **12 of 12**. Output: `artifacts/playwright-real-backend/run-output.txt`.

## Not re-run this attempt
`npm run build` and `npm run lint` (frontend change: two strings; type-checked), and the default mocked Playwright suite (environment-dependent; see R02 evidence).

## Totals
366 + 158 + 12 = 536.
