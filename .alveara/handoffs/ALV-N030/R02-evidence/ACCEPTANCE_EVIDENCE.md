# ALV-N030 R02: acceptance evidence (updated for review R01)

Plan item 63a. The approved briefing set is R04 (sections 1-15) + R06 (section 16) + R07 (section 17). Row numbers in the tables refer to `R01-evidence/TEST_RESULTS.md`; "R02" refers to `R02-evidence/TEST_RESULTS.md`. Changes since R01 are marked **R02**.

## The five acceptance points (plan item 63a)

| # | Acceptance | Status | Evidence |
|---|---|---|---|
| 1 | Every rule and focused test S1 to S17 and guards G1 to G8 in the approved brief pass (plus section 16's S18 to S22) | Met on focused proof. **R02:** S3 is now proven concurrency-safe (CR-1); S21's unreachable-server bound is proven (CR-2); S12, S15, S19 and S20 were re-run on the final tree; and the final notice wording was checked by the client test (CR-3). Not re-run (review R01): the walkthroughs, so the S14 browser check of the notice last ran with the previous wording. | the S and G tables below |
| 2 | No new human staff record can be created without its linked account (legacy remediation only) | Met | S9, S16; the product `POST /api/config/staff` was removed; the fixture-only staff-profile route exists only in TestSupport (G1, G4) |
| 3 | The product host and its publish output contain no test-support code or route | Met | G1, G2, G3, G6 |
| 4 | The upgrade never silently selects a Deployment Administrator or a Practice Owner | Met | S12 (0, 1, 2+), S11; the migration writes no owner |
| 5 | Required coverage: the security matrix, migration tests for 0/1/2+, and the harness migration proven by an owner-chosen walkthrough sample | Met on focused proof | S12; DEMO_EVIDENCE section 1 (the sample and its substitutions) |

## Section 11 matrix (S1 to S17) and section 16 rows (S18 to S22)

| Row | What | Tests | Result (row in TEST_RESULTS) |
|---|---|---|---|
| S1 | `register` and `bootstrap-admin` give 404/405 | `DeploymentSetupApiTests.Sign_up_and_the_old_first_admin_route_no_longer_exist`; auth walkthrough test 1; chain stage 0 S-09a (written, not run) | pass (2, 13); auth 13/13 |
| S2 | setup succeeds once; the same, expired, wrong or late token gets an identical 400 | `DeploymentSetupServiceTests` (valid token, expiry at 14:59 vs 15:00), `DeploymentSetupApiTests.Setup_through_the_cli_and_the_route...` | pass (2, 13) |
| S3 | 5 failures void the token, also under concurrency | `Five_failures_void_the_token_and_record_one_rejection_four_do_not` (sequential 4 vs 5, kept); **R02:** `Simultaneous_failures_are_all_counted_and_the_fifth_voids_the_token_once` (5 and 12 simultaneous attempts: voided, the right token refused, one rejection row, none later, nothing attempted audited) | pass (2); **R02 9/9** |
| S4 | a concurrent race gives one account | `Two_concurrent_setups_with_the_same_token_create_exactly_one_account` | pass (2) |
| S5 | the token is not in logs, audit or the URL | API log and audit assertions; the S14 browser check that the URL does not contain the token | pass (2, 13); S14 |
| S6 | rate-limited | `The_setup_route_is_rate_limited` | pass (13) |
| S7 | the CLI refuses once set up; a reissue voids the old token | `Setup_through_the_cli...`, `Issuing_a_new_token_voids_the_previous_one` | pass (2, 13) |
| S8 | the Deployment Administrator has Admin's permissions and appears in no picker or list | `The_deployment_administrator_has_admin_permissions_a_kind_name_and_is_in_no_picker` | pass (4, 12) |
| S9 | combined create: one transaction, idempotent, no password in logs, the role rule | `Add_staff_member_creates_profile_login_and_link_once...`, `A_fault_after_the_profile_insert_leaves_neither_profile_nor_login`, `The_role_rule...`, `Without_ManageUsers...` | pass (4, 12) |
| S10 | the forced-change gate: a 10-route sample, exactly 5 exempt actions, the stamp rotated | `PasswordChangeGateTests` (6) | pass (3, 12) |
| S11 | the Practice Owner: first designation, then 409, the target rules, the DB refusals | `StaffMemberAndOwnerTests` owner tests; `AccountBootstrapMigrationTests` (one active, never deleted/changed); S14 SetUp walkthrough | pass (1, 4, 12); walkthrough |
| S12 | the upgrade with 0, 1 and 2+ profile-less administrators | `AccountBootstrapMigrationTests` (3 cases + rollback) | pass (1); **R02: 24/24 on the final tree** |
| S13 | guards G1 to G8 | `TestSupportGuardTests` | pass (11, 15) |
| S14 | browser: setup, Add staff member, forced change, notices | `account-bootstrap-real-backend.spec.ts` | 6/6 (slice 6); Fresh 4/4 + SetUp 1/1 (slice 7) |
| S15 | export catalog and patient-owned registry with the new tables | `ExportDomainCatalogTests`, `PatientOwnedDataTests` | pass (1, 4); **R02: 24/24 on the final tree** |
| S16 | no profile-only create, no link edits, legacy-login rules | `Profile_only_create_link_edits...`, `Legacy_login_is_only_for_an_active_profile...` | pass (4, 12) |
| S17 | a reset never sets the flag; lost temporary password recovery | `A_completed_administrator_reset_clears_the_flag...`, `A_lost_temporary_password_is_recovered...` | pass (3, 4, 12) |
| S18 | the mode decision; the probe never migrates **or creates** | `StartupModeTests.S18_...` ("one behind" = the newest history row removed); **R02:** the missing database still does not exist after the probe | pass (13); **R02 9/9** |
| S19 | outside Normal: no app background services, 503s, client files load, exact status bodies; Normal unchanged | `StartupModeTests.S19_...` (3 modes + Normal) | pass (13); **R02 re-run 24/24** |
| S20 | a real process: 201, then exit 75; a failed setup keeps running; a new process starts Normal; entry-point shape | `StartupModeTests.S20_...` (2) | pass (14); **R02 re-run 24/24** |
| S21 | browser: not installed, upgrade required, setup with "restarting", then sign-in | `server-states-real-backend.spec.ts` x2; S14 Fresh | pass. **R02:** the unreachable-server bound is proven by `S21_a_silent_server_...` (67.0 s, 4 connections: every attempt ran to its 15 s timeout, bound 60-82 s) and `S21_the_application_on_a_refusing_server_...` (a real ChainHost process answered `installation_incomplete` after 67.5 s, bound 97 s). This is a process and API proof, as the review allowed. |
| S22 | the dev restart loop | `RestartLoopScriptTests` (4) | pass (15) |

## Guards (section 17.5, R07)

| Guard | What | Result |
|---|---|---|
| G1 | the product host under Development, Production and Test, with every enabling setting, has neither fixture route | pass (10, 11, 15) |
| G2 | the reference graph: only ChainHost and the tests reference TestSupport; the product source never names it | pass |
| G3 | publish list (MSBuild `ComputeResolvedFilesToPublishList`, not a full publish: see Disclosures) | pass |
| G4 | all 8 combinations of the three conditions, for both routes; the staff-profile route's own rules | pass |
| G5 | no dev, deployment, installer, Docker or product file names the setting or the host | pass |
| G6 | the product's OpenAPI document lists no `test-support` path | pass |
| G7 | loopback only: 14 address forms, every configured source; real processes refuse wildcard `CHAIN_URL`, wildcard `ASPNETCORE_URLS` and wildcard `--urls`, and refuse no clock mode | pass |
| G8 | both runners start ChainHost under Test, with test support, an explicit clock mode and `127.0.0.1`; the Vite proxy targets the same address | pass (10, 11, 15) |
