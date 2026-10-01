# ALV-N004 R04 — Changed Files

`git diff --stat 4b99e2a..7d50581` (product changes; review record `4adaf92` and evidence commit are `.alveara/` only).

- `src/Alveara.Api/Architecture/Backup/BackupEntities.cs` - `RestoreProvenAtUtc`.
- `src/Alveara.Api/Architecture/Backup/BackupRestoreService.cs` - `ArchiveDefectCodes`, sticky defect handling in drill and `VerifyFullAsync`, restore proof set only by a successful drill, archive-drill link to the matching history row.
- `src/Alveara.Api/Architecture/Backup/BackupService.cs` - trust/overdue computed from restore-proven backups; retention prefers the restore-proven backup.
- `src/Alveara.Api/Controllers/BackupController.cs` - `restoreProven` on the DTO.
- `src/Alveara.Api/Migrations/*AddRestoreProof*` - nullable column.
- Tests: `BackupRecoveryHardeningTests.cs` (+5), `BackupServiceTests.cs`.
- Frontend: `services/backupApi.ts`, `pages/backup/BackupLists.tsx`, `BackupSettingsForm.tsx`, `BackupStatusPanel.tsx`, `RestoreWizard.tsx`, `pages/BackupRecoveryPage.test.tsx`.
- `docs/operations/BACKUP_AND_RECOVERY.md`.
