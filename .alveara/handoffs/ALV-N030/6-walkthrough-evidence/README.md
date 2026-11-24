# ALV-N030 slice 6: client walkthrough evidence (2026-11-24)

Session CC-20261123-q4vn. Each walkthrough was run alone through `scripts/Invoke-RealBackendWalkthrough.ps1` on the R07 ChainHost path (Test environment, test support, 127.0.0.1), each on a fresh `AlveraWalk_*` database that the runner dropped afterwards.

| Walkthrough | Config | Result | Elapsed | Artifacts |
|---|---|---|---|---|
| S14 account bootstrap | `playwright.account-bootstrap.config.ts` (`-OutputVariable ACCOUNT_BOOTSTRAP_E2E_OUT`) | 6/6 passed (first run) | 30 s | `s14-account-bootstrap/01..07-*.png`, copied from `C:\.alvera-data\Test-Data\Temp\walkthrough-output\AlveraWalk_playwright_account_bootstrap_config_29bb2fb3` |
| Auth (complete) | `playwright.auth.config.ts` | 13/13 passed | 51 s | none (the spec writes no output folder) |

## What S14 proves (brief section 11, row S14)
- **01:** an administrator of an upgraded practice (an existing Admin; no Deployment Administrator, no owner) sees the transition notice with "Designate the Practice Owner".
- **02 to 03:** first-run setup.
  - The token comes from the product's own installer command (`Alveara.Api.dll setup issue-token`), run against the walkthrough database, and is typed into `/setup`. The URL never contains it.
  - Setup leads to sign-in with "Setup is complete".
  - The Deployment Administrator signs in and sees its notice, with the brief's wording, which is still for owner approval.
- **The setup page afterwards** says the installation is already set up and shows no form (no screenshot).
- **04:** "Add staff member" with "Make this person the Practice Owner". There is no login-account picker, and the 16-character temporary password is shown once, with the instruction.
  - The screenshot was taken while the form was still finishing the owner designation ("Saving…" and the success notification).
  - **The password visible in it belongs to a throwaway account in a dropped database, and the next step replaced it with a chosen password.**
- **05:** Security Administration labels: "Deployment Administrator", "Practice Owner", "Must change password".
- **06 to 07:** the new person's first sign-in goes to "Choose a new password", and any other page sends it back there. After the change the dashboard opens.
- **No screenshot:** once an owner exists, the upgraded practice's administrator no longer sees the transition notice.

## Auth walkthrough
All 13 tests pass. The five that slice 5c could not run now pass:
- practice configuration, using the new "Add staff member";
- unsaved-edit protection;
- backup;
- the navigation and permission check for a limited user;
- the account menu.

U-042 (a pre-existing selector, ALV-N026 S3) was fixed as a selector-only change; no product change.
