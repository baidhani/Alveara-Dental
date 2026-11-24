# ALV-N030 R01: real-backend walkthrough evidence

Every walkthrough was run alone through `scripts/Invoke-RealBackendWalkthrough.ps1` on the R07 ChainHost path: `ASPNETCORE_ENVIRONMENT=Test`, `TestSupport__Enabled=true`, `CHAIN_URL=http://127.0.0.1:5072`, and an explicit `CHAIN_CLOCK_MODE`. Each run used a fresh `AlveraWalk_*` database, which the runner dropped afterwards. **The accumulated chain (stage 0 and later) was not run** (owner: left for ALV-N028).

## 1. Slice 5c: the owner-approved five-walkthrough sample, and its substitutions

The owner asked for five proofs: the authentication and forced password change, permission management, staff creation, and two operational flows that create and use different roles.

| Requested proof | Walkthrough as approved | What ran | Result | Time | Commit |
|---|---|---|---|---|---|
| Auth and forced password change | auth (`playwright.auth.config.ts`) | auth | **8 passed** (everything 5c changed: sign-up and bootstrap gone, the fixture administrator, the forced change through the real API, MFA, security administration, the permission matrix, the audit log). Then the run stopped on the obsolete client "Add staff profile" path, planned for slice 6; 5 tests could not run. Before that, U-040 was found and fixed. | 33 s | `0418dc6` |
| Permission management | permission policy | permission policy | 7/7 | 37 s | `0418dc6` |
| Staff creation | reports (an unlinked provider through the real "Add staff member") | reports | 7/7 | 44 s | `0418dc6` |
| Operational flow 1 (a linked staff profile, several roles) | billing | **prescriptions** (owner-approved substitution) | 10/10 | 43 s | `0418dc6` |
| Operational flow 2 (different roles, injected clock) | visit board (`-InjectedClockUtc 2030-01-14T13:30:00Z`) | visit board | 16/16 | 1 m 17 s | `0418dc6` |

- **Billing, substituted.** Its fixture and setup steps passed on the new routes. Its next assertion (`billing-real-backend.spec.ts:168`, "no form before anything is billed") fails identically on the pre-ALV-N030 code (`4e05be3`, that version's own runner, a scratch worktree): **U-041**, pre-existing and **unassigned**. The owner chose prescriptions as the complete linked-profile operational proof; no billing rerun is required for ALV-N030.
- **Auth, completed in slice 6** (the owner rule: rerun only the full auth walkthrough).
- **Corrections the sample found in 5c's own conversion**, fixed before the passing runs:
  - 21 linked staff creates with template-literal bodies were left on the removed route; now converted.
  - Three specs created an UNLINKED provider through the fixture route; they now use the real "Add staff member", as the owner's rule requires.
- **Evidence:** `.alveara/handoffs/ALV-N030/5c-walkthrough-evidence/`, with the README mapping each artifact to its walkthrough.

## 2. Slice 6: the complete auth walkthrough and S14 (commit `62ebdda`)

| Walkthrough | Result | Time | Notes |
|---|---|---|---|
| auth (complete) | **13/13** | 51 s | The 5 tests that 5c could not run now pass: practice configuration with "Add staff member", unsaved-edit protection, backup, the navigation check for a limited user, and the account menu. Before that, **U-042** was found and fixed. |
| S14 account bootstrap | **6/6** | 30 s | setup page (real installer token), refusal after setup, Deployment Administrator notice, Add staff member with the Practice Owner box and the one-time temporary password, Security Administration labels, forced change, transition notice |

Evidence: `6-walkthrough-evidence/`.

## 3. Slice 7: setup mode and the controlled restart (commit `c5f86af`)

| Walkthrough | `-DatabaseState` | Result | Time | Notes |
|---|---|---|---|---|
| S14 | `Fresh` | **4 passed**, 1 skipped (S14-a belongs to SetUp) | 37 s | **Chain host restarts on exit code 75: 1.** setup leads to the 201, then "restarting…", exit 75, the runner restarts the host, the page waits for Normal, then sign-in. |
| S14 | `SetUp` | **1 passed** (S14-a), 4 skipped | 26 s | An upgraded practice's administrator designates the owner through the transition notice; checked through the API and the labels. |
| S21 | `NotInstalled` | 1 passed, 1 skipped | 37 s | the installer message only; the status route answers 503 `installation_incomplete` |
| S21 | `UpgradeRequired` | 1 passed, 1 skipped | 26 s | the upgrade message only; the status route answers 503 `upgrade_required` |
| auth (complete) | `SetUp` | **13/13** | 58 s | Normal mode unchanged |

- **Runs before the passing ones, all fixed:**
  - S14 Fresh, first run: 1 failed. The client's start-up check sent `/login` back to `/setup` after the restart.
  - S14 SetUp, first run: 1 failed. The spec checked a button that hides as soon as the panel opens.
- **Evidence:** `7-walkthrough-evidence/` (S14 screenshots). The S21 screenshots were not kept, because Playwright clears its results folder.

## 4. Not re-run
The final implementation commit `44545eb` (the approved notice wording, plus the S14 check for it) was **not run** by owner instruction. The S14 browser check of the notice last ran with the previous wording.
