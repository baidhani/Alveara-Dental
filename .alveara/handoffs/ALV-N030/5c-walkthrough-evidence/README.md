# ALV-N030 slice 5c: focused walkthrough evidence (owner-approved sample, 2026-11-24)

Session CC-20261123-q4vn. Each walkthrough was run alone through `scripts/Invoke-RealBackendWalkthrough.ps1` on the R07 ChainHost path: `ASPNETCORE_ENVIRONMENT=Test`, `TestSupport__Enabled=true`, `CHAIN_URL=http://127.0.0.1:5072`, and an explicit `CHAIN_CLOCK_MODE`. Each run used a fresh `AlveraWalk_*` database, which the runner dropped afterwards. The code is commit `c867544` plus the spec corrections in `0418dc6`.

## Where each artifact came from

| Folder | Walkthrough (config) | Clock | Result | Elapsed | Copied from |
|---|---|---|---|---|---|
| `permission-policy/` | permission policy (`playwright.permission-policy.config.ts`) | real | 7/7 passed | 37 s | `src/alveara-client/permission-policy-e2e-out/` (the spec's default output folder; now removed) |
| `reports/` | reports (`playwright.reports.config.ts`) | real | 7/7 passed | 44 s | `src/alveara-client/reports-e2e-out/` (now removed) |
| `prescriptions/` | prescriptions (`playwright.prescriptions.config.ts`) | real | 10/10 passed | 43 s | `src/alveara-client/prescriptions-e2e-out/` (now removed) |
| `billing/` | billing (`playwright.billing.config.ts`) | real | setup passed, then the pre-existing U-041 failure (1 passed, 1 failed, 8 not run) | 34 s | `src/alveara-client/billing-e2e-out/` (now removed); only the evidence JSON was written before the failure |
| `visit-board/` | visit board (`playwright.board.config.ts`, `-InjectedClockUtc 2030-01-14T13:30:00Z`) | injected | 16/16 passed | 1 min 17 s | `C:\.alvera-data\Test-Data\Temp\walkthrough-output\AlveraWalk_playwright_board_config_7ff48d25` (the runner's `-OutputVariable BOARD_E2E_OUT` folder) |

## Runs that left no files
- **Auth** (`playwright.auth.config.ts`, real clock): the spec writes no output folder.
  - Final run: 8 passed (everything slice 5c changed), then 1 failed and 4 did not run. The stop is at the Practice Configuration screen's obsolete "Add staff profile" (removed route), which slice 6 replaces; the full auth walkthrough is rerun in slice 6.
  - Earlier runs: 7 passed, then stopped on U-040 (fixed).
  - Elapsed: 1 min 12 s with the build; 32-33 s per rerun.
- **Billing on the pre-ALV-N030 code** (`4e05be3`, scratch worktree, that version's own runner): 1 passed, 1 failed, 8 not run (48 s with the client install). This is the proof that U-041 is pre-existing; its output was not kept.
- **Guards** (backend, not a walkthrough): `TestSupportGuardTests` plus the ChainHost clock tests, 59/59 (38 s).

The timings are the PowerShell wall-clock times around each runner call, so they include database creation, migration, host start and teardown.
