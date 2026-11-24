# ALV-N030 R02: test results (corrections to review R01)

All runs were on the R02 implementation tree (`4428049`; the runs used the working tree that commit captures). Machine: the owner's Windows 11 workstation with SQL Server Express. The commands ran from `src/Alveara.Api.Tests` (or `src/alveara-client` for vitest) after `dotnet build`.

The R01 results (`R01-evidence/TEST_RESULTS.md`) stand for everything R02 did not change. As review R01 directed, these were **not re-run**:
- the five-walkthrough sample and the full auth walkthrough;
- the broad backend regression, the full client suite and the accumulated chain;
- ALV-N013's production-supervisor proof.

## CR-1: the five-failure token limit is atomic

| Command | Result | Time |
|---|---|---|
| `dotnet test --no-build --filter "FullyQualifiedName~Alveara.Api.Tests.DeploymentSetupServiceTests.\|FullyQualifiedName~StartupModeTests.S18"` | **9/9** | 56 s |

The 9 tests are the six existing setup-service tests (the **sequential 4-versus-5 boundary test is kept**), plus the new `Simultaneous_failures_are_all_counted_and_the_fifth_voids_the_token_once` for **5** and **12** simultaneous wrong attempts, plus S18.

What the new test proves:
- each attempt runs on its own context, and all are released together through a start gate;
- the token is voided, with `FailedAttempts` exactly 5 (with exactly five attempts, a lost update would leave it below 5 and the token usable);
- the right token is then refused;
- there is exactly one `SetupTokenRejected` row, and three later failures add none;
- no attempted token, username or password appears in any audit detail.

## CR-2: an unreachable server ends the readiness check within its bound

| Command | Result | Measured | Bound |
|---|---|---|---|
| `dotnet test --no-build --filter "FullyQualifiedName~StartupModeTests.S21" --logger "console;verbosity=normal"` (together with the CR-1 group above in one run of 3 m 37 s) | 2/2 | see below | |
| `S21_a_silent_server_runs_every_attempt_to_its_timeout_and_the_check_ends_within_its_bound` | pass | **67.0 s, 4 connections** | 60-82 s; at least 4 connections |
| `S21_the_application_on_a_refusing_server_starts_not_installed_within_its_bound` | pass | **answered after 67.5 s** | 97 s |

- **The silent-server test.** A local listener accepts every connection and never answers, so each attempt runs to the probe's 15 s timeout.
  - The probe makes the first attempt plus 3 retries with 1/2/4 s backoff and answers NotInstalled.
  - The lower bound (4 × 15 s) proves the timeouts ran; the connection count proves the retries ran; the upper bound proves it does not hang.
- **The refusing-server test.** It starts the whole application, a real ChainHost process, against a server port that refuses connections. The status route answers 503 `{"mode":"installation_incomplete"}` within the bound.
- **Nothing created.** On a reachable server, S18 now also checks that the missing database still does not exist after the probe. Nothing can be created or migrated on an unreachable one.
- **A defect found and fixed by this proof.** The first run of the silent-server test **failed: 37.1 s, 2 connections**.
  - **Cause:** SqlClient's connection pool. After a failed open, the pool's blocking period failed the next opens at once with the cached error, without trying the server, so retries 3 and 4 did nothing.
  - **Fix:** the probe now opens without pooling (`Pooling = false`, `StartupMode.cs`). The rerun above passed.

## CR-3: final-tree refresh

| Command | Result | Time |
|---|---|---|
| `dotnet test --no-build --filter "FullyQualifiedName~StartupModeTests.S19\|FullyQualifiedName~StartupModeTests.S20\|FullyQualifiedName~Alveara.Api.Tests.AccountBootstrapMigrationTests.\|FullyQualifiedName~Alveara.Api.Tests.ExportDomainCatalogTests.\|FullyQualifiedName~Alveara.Api.Tests.PatientOwnedDataTests."` | **24/24** | 1 m 24 s |
| `npx vitest run src/pages/AccountBootstrap.test.tsx` (client) | **23/23**, including "tells the Deployment Administrator to add the owner as a staff member" (the final approved wording, through the same constant) | 5 s |

- **What the 24 cover:**
  - **S12:** the 0, 1 and 2+ profile-less administrator cases, the rollback, and the one-active and protected-designation rules (AccountBootstrapMigrationTests, 7);
  - **S15:** the export catalog and the patient-owned registry;
  - S19 (4) and S20 (2). These were re-run because the pooling fix touches the start-up check that every host runs.
- **Mark.** All of them now run with slice 7's setup-completed mark in place, which closes R01's "not re-run after slice 7" gap.
