# ALV-N004 R04 — Test Results

Implementation commit `7d50581a8ff7e458da286ff9922456e75410317a`.

## Backend — `dotnet test` (full)
**371 of 371 passing, 0 failed, 0 skipped** (16 m 12 s). Real SQL Server (LocalDB) BACKUP/RESTORE, real files, real OpenPGP, real GnuPG.

| Class | Tests |
|---|---|
| BackupCryptoTests | 22 |
| BackupServiceTests | 20 |
| BackupRestoreTests | 20 |
| BackupSchedulerTests | 9 |
| BackupApiTests | 12 |
| BackupRecoveryHardeningTests | 26 (5 new this attempt) |

## Frontend
`npx vitest run`: **158 of 158**; `tsc --noEmit` clean; `npm run build` clean; `npm run lint` exit 0 (pre-existing warnings only).

## Real-backend, real-browser
Fresh `AlveraE2E`, real `dotnet run --no-build` API, `npx playwright test --config=playwright.auth.config.ts`: **12 of 12**. Output: `artifacts/playwright-real-backend/run-output.txt`.

## Not run
Default mocked `npx playwright test` (environment-dependent; see R02 evidence).

## Totals
371 + 158 + 12 = 541.
