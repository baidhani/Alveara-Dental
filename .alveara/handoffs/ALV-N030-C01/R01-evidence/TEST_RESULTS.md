# ALV-N030-C01 R01: test results

- **Machine:** the owner's Windows 11 workstation, SQL Server Express (`.\SQLEXPRESS`), .NET 10, Node 24.
- **Where commands run from:**
  - backend commands from `src/Alveara.Api.Tests` after `dotnet build`, as `dotnet test --nologo --no-build --filter "<filter>"`;
  - client commands from `src/alveara-client`;
  - browser walkthroughs from the repository root, through `scripts/Invoke-RealBackendWalkthrough.ps1` (ChainHost under Test on a fresh `AlveraWalk_*` database, removed afterwards).
- **Times** are measured wall-clock times.
- **Each run was shown to the owner and approved** before it ran (the owner's rule: code first, then the focused proof).

| Slice | Run (filter or command) | Result | Time | Tree |
|---|---|---|---|---|
| 1 | `PracticeOwnerInvariantTests.` + `StaffMemberAndOwnerTests.` | **24/24** | 1 m 44 s | `d1dd0b4` |
| 1 | `AccountBootstrapMigrationTests.` + `PasswordChangeGateTests.` | **13/13** | 1 m 12 s | `d1dd0b4` |
| 2 | `PracticeOwnerCreationTests.` + `StaffMemberAndOwnerTests.` | **21/21** | 1 m 34 s | `c17cbea` |
| 2 | `npx vitest run src/pages/AccountBootstrap.test.tsx` | **24/24** | about 10 s | `c17cbea` |
| 3 | `PracticeOwnerTransferTests.` + `PracticeOwnerInvariantTests.` + `PracticeOwnerCreationTests.` + `StaffMemberAndOwnerTests.` | **45/46**, then **S40 1/1** (see note a) | 3 m 20 s, then 23 s | `7ba61eb` |
| 3 | `StaffInactivationServiceTests.` + `StaffInactivationAlertTests.` + `AuditLogApiTests.` + `AccountServiceMfaTests.` | **49/49** | 3 m 40 s | `7ba61eb` |
| 4 | `DeploymentRecoveryTests.` + `DeploymentSetupServiceTests.` + `DeploymentSetupTests.` + `AccountBootstrapMigrationTests.` + `AuthorizationArchitectureTests.` | **28/28** | 1 m 47 s | `40bea9d` |
| 5 | `OwnerInvariantAuditTests.` + `CheckoutTests.` + `VisitCheckoutMigrationTests.` + `AccountBootstrapMigrationTests.` + `PasswordChangeGateTests.` | **74/75**, then **S29 1/1** (see note b) | **5 m 24 s** (over the 5-minute rule; R01.md disclosure 4), then 23 s | `9587462` |
| 6 | `npx vitest run` for OwnerAdministration, AccountBootstrap, ConfigEntityPanel, AdminUsersPage and AdminUserDetailPage | **59/59** | 8 s | `1d952d9` |
| 6 | `OwnerInvariantAuditTests.` | **2/2** | 31 s | `1d952d9` |
| 6 | Walkthrough `-Config playwright.account-bootstrap.config.ts -OutputVariable ACCOUNT_BOOTSTRAP_E2E_OUT -DatabaseState Fresh` (S14-b to S14-e) | **4 passed**, 1 skipped by design (S14-a runs on SetUp) | 45 s | `1d952d9` |
| 6 | The same with `-DatabaseState SetUp -SkipBuild` (S14-a) | first run 1 failed (see note c); rerun **1 passed**, 4 skipped by design | 34 s, then 30 s | `1d952d9` |
| 6 | Walkthrough `-Config playwright.owner-transfer.config.ts -OutputVariable OWNER_TRANSFER_E2E_OUT -DatabaseState SetUp -SkipBuild` (C01-a to C01-c) | **3/3** | 35 s | `1d952d9` |

**Client static checks on the final tree:**
- `npx tsc -p tsconfig.app.json --noEmit` is clean.
- `oxlint` gives no new warnings in the C01 files, apart from the `no-unsafe-optional-chaining` pattern that the existing `AccountBootstrap.test.tsx` already uses.

**Notes on the three first runs that failed.** In each case the product was right and the test was wrong. The test was corrected, never loosened, and rerun:

a. **S40** requested `/api/audit-log`; the route is `/api/auth/audit-log`, so the request returned the client's HTML page. The URL was corrected in the test.

b. **S29** walked its visits to checkout *after* re-linking the booked provider to the new login. The scheduling helper then was no longer the booked provider ("Only the appointment's booked provider can start or end treatment"). The visits are now walked first, as the existing treating-provider test does.

c. **S14-a** asserted `toBeDisabled()` on an `<option>`. Playwright's own call log shows the element rendered as `<option disabled …>upgrade-dentist-… - change this person's role…</option>`, but `toBeDisabled` does not report a disabled option. The test now asserts the `disabled` attribute and the option text.

**Test databases:**
- After every run the count of `AlveraTest_*` databases was back to the one that predates ALV-N030 (`AlveraTest_b62446e8…`, 2026-11-16).
- The exception is the slice 5 run, whose leftover was dropped (R01.md disclosure 4).
- No `AlveraWalk_*` database remains.

**Not run, by design** (R10): broad backend regression, the full client suite, other walkthroughs, the accumulated chain, and any owner walkthrough.
