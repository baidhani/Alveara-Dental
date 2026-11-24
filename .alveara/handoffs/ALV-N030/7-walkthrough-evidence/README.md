# ALV-N030 slice 7: setup mode and the controlled restart, walkthrough evidence (2026-11-24)

Session CC-20261123-q4vn. Each walkthrough was run alone through `scripts/Invoke-RealBackendWalkthrough.ps1` on the R07 ChainHost path. The new `-DatabaseState` parameter chose the database the host started on. Each run used a fresh `AlveraWalk_*` database, which the runner dropped afterwards.

| Walkthrough | `-DatabaseState` | Result | Elapsed | Artifacts |
|---|---|---|---|---|
| S14 account bootstrap | `Fresh` | 4 passed, 1 skipped (S14-a belongs to the SetUp run). **Chain host restarts on exit code 75: 1** | 37 s | `s14-fresh/`, from `AlveraWalk_playwright_account_bootstrap_config_f206ad70` |
| S14 account bootstrap | `SetUp` | 1 passed (S14-a), 4 skipped (they belong to the Fresh run) | 26 s | `s14-setup/`, from `AlveraWalk_playwright_account_bootstrap_config_939bc8af` |
| S21 server states | `NotInstalled` | 1 passed, 1 skipped | 37 s | none kept (see below) |
| S21 server states | `UpgradeRequired` | 1 passed, 1 skipped | 26 s | none kept |
| Auth (complete) | `SetUp` (default) | 13/13 passed (Normal mode unchanged) | 58 s | none (the spec writes no output folder) |

## What the runs prove
- **S14 Fresh (brief S14 and the third state of S21).** The runner left the database never set up, so the chain host started in Setup mode.
  - The token from the product's installer command was typed into `/setup`.
  - The 201 came back, then the page showed "Setup is complete. DentalKeep is restarting…" (`02b-restarting.png`). The chain host exited with **75** and the runner started it again.
  - The page waited until the status route said Normal, then went to sign-in.
  - Then: the Deployment Administrator's notice, "Add staff member" with the Practice Owner box, the temporary password shown once, the Security Administration labels, and the forced change.
  - The temporary password in `04-temporary-password.png` belongs to a throwaway account in a dropped database, and the next step replaced it.
- **S14 SetUp:** an administrator of an upgraded practice (set up, no owner) designates the Practice Owner through the transition notice. The notice goes away, `GET /api/practice-owner` names the person, and Security Administration labels them.
- **S21:** with no database, and with a pending migration, the status route answers its 503 mode body. The client shows only the installer or upgrade message, with no sign-in form, and `/api/patients` answers 503.
  - The screenshots went to Playwright's own results folder, which Playwright clears on every run, so they were not kept; the pass results and timings are the evidence.

## Corrections the runs found (all fixed before the passing runs above)
- **Client.** The start-up check read the server's mode only once, at load. After setup and the restart, it sent `/login` back to `/setup`. It now asks again when the page leaves `/setup` (vitest case added).
- **Spec.** S14-a's first version checked that a button was gone, and that button hides as soon as the panel opens. It now waits for the whole notice to go and reads the owner back from the API.
