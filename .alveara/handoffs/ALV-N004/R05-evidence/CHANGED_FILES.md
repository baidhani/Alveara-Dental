# ALV-N004 R05 — Changed Files

Product changes `7d50581..2d98193`:
- `src/Alveara.Api/Architecture/Backup/BackupEntities.cs` - `ArchiveDefectCode/AtUtc`, `HasArchiveDefect`, `MarkVerificationFailed`, `ArchiveDefects`.
- `src/Alveara.Api/Architecture/Backup/BackupService.cs` - `VerifyHashAsync` guarded; failures via `MarkVerificationFailed`.
- `src/Alveara.Api/Architecture/Backup/BackupRestoreService.cs` - verify-only/drill paths use the durable defect.
- `src/Alveara.Api/Controllers/BackupController.cs` - `archiveDefectCode` on the DTO.
- `src/Alveara.Api/Migrations/*AddArchiveDefect*` - two columns + backfill.
- `src/Alveara.Api.Tests/BackupRecoveryHardeningTests.cs` (+4).
- `src/alveara-client/src/services/backupApi.ts`, `pages/BackupRecoveryPage.test.tsx`.
- `docs/operations/BACKUP_AND_RECOVERY.md`.
