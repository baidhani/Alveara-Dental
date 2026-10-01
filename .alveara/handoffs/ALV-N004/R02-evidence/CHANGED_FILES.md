# ALV-N004 R02 — Changed Files

`git diff --stat 8089954..f8969c2`: **33 files changed, 2800 insertions(+), 463 deletions(-)**.

Backend (`src/Alveara.Api/`): `Alveara.Api.csproj` (BouncyCastle), `Architecture/Backup/BackupCrypto.cs` (OpenPGP rewrite), `BackupEntities.cs`, `BackupInfrastructure.cs` (`ImportDirectory`), `BackupRestoreService.cs` (archive sources, deployment checks), `BackupService.cs`, `BackupSupport.cs`, **new** `DeploymentSettings.cs` (settings, source, monitor, guard), `Controllers/BackupController.cs` (archive endpoints), `Controllers/SystemStatusController.cs`, `Data/AlveraDbContext.cs`, `Program.cs`, **new** migration `AddRecoveryHardening`.

Tests (`src/Alveara.Api.Tests/`): `BackupCryptoTests.cs` (rewritten), `BackupApiTests.cs`, `BackupRestoreTests.cs`, `BackupSchedulerTests.cs`, `BackupServiceTests.cs`, `BackupTestSupport.cs`, **new** `BackupRecoveryHardeningTests.cs`.

Frontend (`src/alveara-client/`): `services/backupApi.ts`, `pages/backup/RecoveryKeyPanel.tsx`, `RestoreWizard.tsx`, `backupMessages.ts`, **new** `ArchivePanel.tsx`, `pages/BackupRecoveryPage.tsx`, `pages/SystemStatusPage.tsx`, `hooks/useSystemStatus.ts`, `pages/BackupRecoveryPage.test.tsx`, `e2e/auth-real-backend.spec.ts`.

Docs: `docs/operations/BACKUP_AND_RECOVERY.md` (rewritten for OpenPGP, generated passphrase, archive recovery, deployment settings). `.alveara/` status files are updated in the evidence commit.
