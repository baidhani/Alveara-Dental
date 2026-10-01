# ALV-002-C01 R01 — Test Results

All commands run from the repository at implementation commit `ef19d25cb8c1d687d56bb489c379291a92db301c`.

## Backend — `dotnet test`

```
dotnet test src/Alveara.Api.Tests/Alveara.Api.Tests.csproj
```

**Result: 195 of 195 passing, 0 failed, 0 skipped.**

New this attempt:

- `ConcurrencyGuardTests.cs` (3 tests): stale-read rejection against `StaffProfile.RowVersion`, `ToProblem()`'s stable shape, current-version save succeeds normally.
- `IdempotencyGuardTests.cs` (4 tests): not-yet-processed → processed transition, per-command-type scoping, database-unique-constraint duplicate rejection, recorded-timestamp persistence.
- `RecordLifecycleGuardTests.cs` (3 tests): allowed transition passes, disallowed transition throws `UnknownLifecycleTransitionException`, empty allowed-set rejects every action.
- `AuditLogImmutabilityTests.A_business_change_cannot_commit_if_its_coupled_audit_write_is_invalid` (1 test, extending the existing file): proves the audit/business-write transactional-coupling policy.

An intermediate run (before the `StaffProfile` correction) surfaced 3 genuine `ALV-001-C01` regressions when the concurrency token was first placed on `UserAccount` directly - see `R01.md`'s "A genuine conflict found and resolved" section. Those are not present in the final 195/195 count; the fix (moving the demonstration to `StaffProfile`) is what's committed.

Every test from before this attempt (179, carried from `STORY-002`/`ALV-N009`/`ALV-001-C01`/`ALV-N002`/`STORY-001`) continues to pass, including the full `AccountServiceRoleChangeTests.cs` suite now exercising the shared `AuditService.Record` path internally with no observable change.

## Backend — `dotnet build`

Clean, 0 warnings, 0 errors.

## Real-backend, real-browser — `npx playwright test --config=playwright.auth.config.ts`

```
cd src/Alveara.Api
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "IF DB_ID('AlveraE2E') IS NOT NULL BEGIN ALTER DATABASE AlveraE2E SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE AlveraE2E; END"
dotnet ef database update --connection "Server=(localdb)\MSSQLLocalDB;Database=AlveraE2E;..."
ConnectionStrings__Alveara="...AlveraE2E..." AdminBootstrapSecret="e2e-real-backend-secret" dotnet run --no-build

# second terminal, from src/alveara-client
E2E_BOOTSTRAP_SECRET="e2e-real-backend-secret" npx playwright test --config=playwright.auth.config.ts
```

**Result: 8 of 8 passing**, unchanged in assertion from prior attempts, against a genuinely fresh, migrated database and a real running API process built from this attempt's implementation commit. Raw output: `R01-evidence/artifacts/playwright-real-backend/run-output.txt`.

## Frontend

Not touched this attempt (no frontend file in the diff); frontend suites were not rerun since there is nothing to re-verify.
