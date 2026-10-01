# ALV-N004 R02 — Next Story Impact

Next authorized item after approval and closure: **GATE A — Foundation Ready** (mandatory stop), not `STORY-003`. Row A9 evidence is the drill evidence above, now including history-free recovery and deployment invariants; the gate is not evaluated here.

Later stories must:
- Register new persistent stores as an `IBackupAssetSource` **and** in `ManagedAssetClasses.All`; any new **deployment-owned setting required to interpret restored data** must be added to the `DeploymentSettings` allowlist (and `DifferencesFrom`/`RequiredSettings`), otherwise recovery silently omits it.
- Add post-restore integrity checks to `BackupRestoreService.ValidateRestoredApplicationAsync`.
- Treat the backup file as OpenPGP: `gpg --decrypt` with the recovery key restores the ZIP without Alveara.
- Not force a Data Protection application name; if an installation sets `DataProtection:ApplicationName`, it is recorded and enforced on recovery.

Assumptions: backups from the rejected R01 format are not supported (never released). Restore targets share the SQL Server instance; staging/restore folders must be accessible to the SQL Server service account. No Execution Plan prompt change is required.
