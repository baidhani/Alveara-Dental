# ALV-N004 R01 — Changed Files

`git diff --stat 671287b..bd50b4a` (ALV-N003 closure commit → ALV-N004 implementation commit): **39 files changed, 7196 insertions(+), 2 deletions(-)**.

## Backend — new (`src/Alveara.Api/`)

| File | Purpose |
|---|---|
| `Architecture/Backup/BackupCrypto.cs` | recovery key pair, AES-256-GCM chunked encryption/authenticated decryption with RSA-OAEP key wrapping |
| `Architecture/Backup/BackupEntities.cs` | settings, backup record, restore drill, notification entities; `ManagedAssetClasses` |
| `Architecture/Backup/BackupInfrastructure.cs` | `BackupPaths`, `IBackupAssetSource`/`FileTreeAssetSource`, `IDatabaseRestoreProvider`/`SqlServerRestoreProvider` (restore only to a new database) |
| `Architecture/Backup/BackupSupport.cs` | exception type, manifest, notifier interface + drop-file notifier, database facts, failure classification |
| `Architecture/Backup/BackupService.cs` | settings, recovery key, run backup (idempotent), retention, hash verification, interrupted-run recovery, status |
| `Architecture/Backup/BackupRestoreService.cs` | preflight, full verification, restore drill with post-restore validation, target removal |
| `Architecture/Backup/BackupScheduling.cs` | slot scheduler, scheduled-backup job handler, scheduler hosted service |
| `Controllers/BackupController.cs` | `api/backup/*` |
| `Migrations/20261001130139_AddBackupAndRecovery(.Designer).cs` | 4 tables |

## Backend — modified

`Architecture/Identity/AccountService.cs` (`ReauthenticateAsync`; public MFA protector purpose constant), `AuditLogEntry.cs` (`SensitiveActionStepUpFailed`), `Permission.cs` / `PermissionMatrix.cs` (`ManageBackups`, `ViewBackupStatus`), `Data/AlveraDbContext.cs`, `Program.cs` (DI, fixed Data Protection application name), `Migrations/AlveraDbContextModelSnapshot.cs`.

## Backend tests — new

`BackupCryptoTests.cs`, `BackupServiceTests.cs`, `BackupRestoreTests.cs`, `BackupSchedulerTests.cs`, `BackupApiTests.cs`, `BackupTestSupport.cs` (81 cases).

## Frontend — new

`services/backupApi.ts`; `pages/BackupRecoveryPage.tsx/.css`; `pages/backup/{BackupStatusPanel,RecoveryKeyPanel,BackupSettingsForm,BackupLists,RestoreWizard}.tsx`, `backupMessages.ts`, `settingsRules.ts`.

## Frontend — modified

`App.tsx` (route `/admin/backup`, gated `ViewBackupStatus`), `app/moduleRegistry.ts` (nav entry).

## Frontend tests

New `pages/BackupRecoveryPage.test.tsx` (32); extended `App.routeGuards.test.tsx` (+2) and `e2e/auth-real-backend.spec.ts` (+1 real backup/restore test; limited-permission test extended).

## Documentation

New `docs/operations/BACKUP_AND_RECOVERY.md` (runbook).

No file under `.colaberry/`, `CLAUDE.md`, `index.html`, `assets/`, `GPT Docs/`, or `.alveara/QUALITY_GATES.md` was touched by the implementation commit.
