# ALV-N004 R03 — Changed Files

`git diff --stat f8969c2..4b99e2a`: see below (8 files).

- `src/Alveara.Api/Architecture/Backup/DeploymentSettings.cs` - `DeploymentInvariantStore`, status `Verifier`, serialized monitor check, positively-verified guard with `deployment_unverified`.
- `src/Alveara.Api/Architecture/Backup/BackupService.cs` - `EnsureDeploymentAgreesAsync` before/after snapshot; manifest uses the captured settings.
- `src/Alveara.Api/Architecture/Backup/BackupRestoreService.cs` - `deployment_asset_matches_manifest`, `restored_database_deployment_matches_manifest`.
- `src/Alveara.Api.Tests/BackupRecoveryHardeningTests.cs` (7 new tests), `BackupTestSupport.cs` (reset clears `DeploymentInvariants`).
- `src/alveara-client/src/pages/SystemStatusPage.tsx`, `pages/backup/backupMessages.ts` - unverified wording and message.
- `docs/operations/BACKUP_AND_RECOVERY.md` - consistency and verified-before-serve sections.
