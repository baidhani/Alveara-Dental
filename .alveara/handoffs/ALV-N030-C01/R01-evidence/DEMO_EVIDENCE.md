# ALV-N030-C01 R01: demo evidence (real browser, real application)

All three runs used `scripts/Invoke-RealBackendWalkthrough.ps1`, which runs ChainHost under Test on a fresh `AlveraWalk_*` database that is removed afterwards. Implementation tree: `1d952d9`.

| Run | Proves | Screenshots (kept outside the repository) |
|---|---|---|
| S14, `-DatabaseState Fresh` | first-run setup with the installer's token, the controlled restart, sign-in; **the owner box locks Role to Administrator with the explanation**; the owner added with a temporary password; the forced password change | `C:\.alvera-data\Test-Data\Temp\walkthrough-output\AlveraWalk_playwright_account_bootstrap_config_ac849f45\` |
| S14, `-DatabaseState SetUp` | an upgraded practice: **a Dentist is listed but disabled** ("change this person's role to Administrator first"); an Administrator staff member is designated through the transition notice | `...\AlveraWalk_playwright_account_bootstrap_config_6ced9b98\` |
| Transfer and recovery, `-DatabaseState SetUp` | **C01-a:** a token from the product's own `recovery issue-token` command, typed into `/recovery`, creates the Deployment Administrator (not in the URL). **C01-b:** the owner's role is locked; the Transfer Practice Owner dialog (reason, password, confirmation) moves ownership A to B; A's open session is sent to sign-in. **C01-c:** A disables the Deployment Administrator; a new console token resets it on `/recovery`; it signs in with the new password. | `...\AlveraWalk_playwright_owner_transfer_config_fa3aee7f\` (`01-recovery-created.png`, `02-transfer-dialog.png`, `03-transferred.png`, `04-recovery-reset.png`) |

The screenshots stay on the workstation (the runner's output folder). They are not part of the repository or this package.

**Not done by design:** an owner manual walkthrough. R08-4 and R10 make it optional for C01. The real-authenticator QR walkthrough is a required gate of ALV-N030-C02, not C01.
