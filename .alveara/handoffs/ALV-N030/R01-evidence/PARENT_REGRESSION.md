# ALV-N030 R01: parent and program regression

- **Parent regression:** n/a. ALV-N030 is a new production story with no parent course story.
- **Program regression:** `fullRegression: DEFERRED_TO_ALV-N028`, not release-certified (the post-Gate-E deferred-regression route, `.alveara/PROGRAM_REGRESSION_LEDGER.md`).

## What ALV-N028's broad regression must cover for ALV-N030
- **Every user of the shared test infrastructure changed here, not only the sampled classes.** The changed pieces are:
  - `TestDatabaseFixture`, which marks every test database as set up;
  - `TestAccounts` and `TestStaff`, the direct fixtures;
  - `IdentityTestHelpers`, `SchedulingTestSupport`, `SafetyTestBase`, `CompletionTestBase` and `SchedulingApiHarness`, all of which use them.
- **Every real-backend walkthrough on the new ChainHost runner path.** The 5c sample, the auth walkthrough and S14/S21 ran here; the rest did not.
- **The accumulated chain**, including the rewritten stage 0 (S-09a to S-09c and C-01). It was not run in ALV-N030.
- **S12 and S15 after the slice 7 fixture change** (see `TEST_RESULTS.md`).
- **The known pre-existing reds that remain:**
  - U-041 (the billing walkthrough assertion, unassigned);
  - U-018 (the flaky BillingStatementsPanel vitest).
