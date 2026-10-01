# ALV-N004 R02 — Test Results

Implementation commit `f8969c29d29c2c924ac05b725de6935bc739eef5`.

## Backend — `dotnet test Alveara.slnx`
Full run: **358 of 358 passing, 0 failed, 0 skipped** (15 m 32 s). One test was added after that run began (`A_recovered_application_resolves_practice_local_times_identically_only_with_the_recorded_time_zone_and_refuses_data_requests_otherwise`); the `BackupRecoveryHardeningTests` class was re-run on the final build: **14 of 14 passing**. The solution lists 359 tests. All backup tests use real SQL Server (LocalDB) BACKUP/RESTORE, real files and real OpenPGP; the interop test runs a real GnuPG 2.4.9 and fails (does not skip) if it is absent.

| Class | Tests |
|---|---|
| BackupCryptoTests | 22 |
| BackupServiceTests | 20 |
| BackupRestoreTests | 20 |
| BackupSchedulerTests | 9 |
| BackupApiTests | 12 |
| BackupRecoveryHardeningTests | 14 (new) |

## Frontend
`npx vitest run`: 25 files, **158 of 158 passing** (R01: 156; recovery-key tests reworked for the generated passphrase, 2 archive-recovery tests added). `tsc --noEmit` clean. `npm run build` clean. `npm run lint` exit 0, only pre-existing warnings (none in changed files).

## Real-backend, real-browser
Fresh `AlveraE2E` database (`dotnet ef database update`), real `dotnet run --no-build` API with `AdminBootstrapSecret` and `Backup__Root`, then `npx playwright test --config=playwright.auth.config.ts`: **12 of 12 passing**. Output: `artifacts/playwright-real-backend/run-output.txt`.

## Not run / not part of this evidence
The default `npx playwright test` (mocked `shell`, `accessibility`, `system-status` specs plus the real-backend spec without a backend) fails in this environment with `/api/auth/permissions` proxy errors; it was not part of any earlier attempt's evidence and was not baseline-compared here.

## Totals
359 backend + 158 frontend + 12 browser = 529.
