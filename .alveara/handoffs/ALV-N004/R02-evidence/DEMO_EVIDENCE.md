# ALV-N004 R02 — Demo Evidence

Real Chromium + real API + real SQL Server (Playwright test 10, `artifacts/playwright-real-backend/run-output.txt`):

1. Setting up the recovery key no longer asks for a passphrase: after confirming the current password the key (an armored **PGP PRIVATE KEY BLOCK**) and a **generated passphrase** appear once; both are cleared on acknowledgement and never stored.
2. A real backup is made (Contains: Database, Deployment settings, Documents, Encryption keys; coverage complete); the restore wizard restores it into an isolated database; the drill counts as full verification; the target is removed with the password.
3. **Disaster-recovery path:** the **Recover from a retained backup file** panel lists the retained `.abk` (found on disk, not from history); the wizard restores from it with the same recovery material (no verify-only shortcut), and the result lists `deployment_settings_match_this_server` as passed.

Operator procedure: `docs/operations/BACKUP_AND_RECOVERY.md` ("Disaster recovery on a replacement server (no backup history needed)", "Deployment settings"). Automated disaster-recovery proof (history-free, app started, sign-in, MFA, documents; non-default time zone): `BackupRecoveryHardeningTests`.
