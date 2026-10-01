# Backup, verification and recovery runbook

Operational procedure for Alveara's encrypted full-state backups (ALV-N004). Everything here can be done
from the **Backup & recovery** admin page (`/admin/backup`); the API and automated tests prove each step.

## What a backup contains

One file, `alveara-backup-<UTC time>-<id>.abk`, holding every **persistent asset class the application
currently manages** (the registry is `ManagedAssetClasses`; a later story that adds a new on-disk store must
register an `IBackupAssetSource` and add its class there, which makes older backups visibly incomplete):

| Asset class | What | How it is captured |
|---|---|---|
| `database` | the whole SQL Server database (identity, audit trail, configuration, backup history, ...) | SQL Server's own `BACKUP DATABASE ... WITH CHECKSUM` - transactionally consistent while the app is running; never a raw file copy |
| `documents` | the blob/document storage root (`StorageRoot`) | every file copied with a SHA-256 recorded in the manifest (in-flight `.tmp-` uploads excluded) |
| `dataProtectionKeys` | the ASP.NET Core Data Protection key ring (`DataProtectionKeysPath`) | every key file copied with its SHA-256 - **without these, restored accounts could not decrypt their MFA secrets** |

Inside the encrypted file is a ZIP of those assets plus `manifest.json` (per-file hashes, schema migration,
app version, table row counts around the snapshot).

## Encryption and the recovery key (read this once, carefully)

- Content is encrypted with **AES-256-GCM** (authenticated: any change, truncation, extension or reordering
  fails). Each backup uses a fresh random data key, wrapped with **RSA-3072 / OAEP-SHA256** to the **recovery
  public key**. Only platform cryptography is used; the only thing implemented is the minimal chunk framing
  for large files (see `BackupCrypto`).
- The server stores **only the public key**. It can therefore create backups unattended but can **never read
  one back**. The private half - the **recovery key file** - is shown to the administrator **once**, at setup,
  already encrypted with a passphrase the administrator chooses (PBES2/PBKDF2-SHA256 600k + AES-256).
- **Required recovery material must never exist only inside a backup or on the server.** Store BOTH of these
  offline, away from the server (e.g. a safe, plus a second copy elsewhere): (1) the recovery key file,
  (2) its passphrase. Losing either makes every backup made with that key unrecoverable. Nobody - including
  the developers - can recover them.
- Replacing the key affects only **future** backups; keep the old key and passphrase for old backups.

## Setting up (once)

1. Sign in as an administrator, open **Backup & recovery**, choose **Set up recovery key**.
2. Pick a strong passphrase (12+ characters) and confirm your current password.
3. **Download the key file**, store it and the passphrase offline, tick the confirmation, press Done. The key
   is cleared from the screen and cannot be shown again.
4. Review **Schedule, retention and verification policy** (defaults: daily, keep 7, 2 verifications before
   scheduled backups are trusted, re-verify every 30 days) and the **backup folder**. Prefer a different
   physical disk than the database. Off-site/cloud targets are a future extension.

Server-side configuration (all optional, `appsettings` / environment): `Backup:Directory` (default
destination), `Backup:StagingRoot` and `Backup:RestoreRoot` (scratch space - **the SQL Server service account
must be able to read and write both**), `Backup:NotificationDirectory`, or `Backup:Root` to relocate them all.

## Verification levels (what each one proves)

| Level | Needs the key? | Proves | Does not prove |
|---|---|---|---|
| Written + re-read (every backup) | no | the file on disk matches the hash recorded at creation | that it can be decrypted or restored |
| **Check file hash** (any time) | no | the bytes are unchanged since creation (detects bit-rot/tampering/truncation) | restorability |
| **Verify only** (wizard) | yes | decrypts and authenticates; every component matches the manifest; SQL Server accepts the database backup (`RESTORE VERIFYONLY`); schema/format compatible | that the application runs on it |
| **Restore drill** (wizard) | yes | all of the above **plus** a real restore into an isolated database and post-restore validation (below) | - |

History keeps *backup succeeded*, *verification state* and *failure* as separate facts. A backup that has only
been hash-checked is shown as **"File hash verified only"**, never as "verified".

### Probation (confidence workflow)

Scheduled backups run from day one, but are labelled **on probation** until `RequiredSuccessfulVerifications`
full verifications/restore drills have passed. A drill older than `VerificationCadenceDays` is flagged
**overdue**. A failed verification never counts.

## Restore drill procedure (do this after setup, then at the cadence you chose)

1. **Backup & recovery -> history -> Verify / restore...** on a successful backup.
2. Provide the **recovery key file** (or paste it), its **passphrase**, and your **current password**.
3. **Check compatibility and recovery material.** Every check must pass: backup retained, file present and
   matching its hash, key belongs to this backup. Fix anything that fails (a wrong key is reported as such).
4. Choose **Restore into an isolated target** (tick the acknowledgement). This creates a **new** database
   `AlveraRestore_<id>` and a private folder under `Backup:RestoreRoot`. **It never touches live data.**
5. Read the result. Blocking checks (all must pass):
   `format_supported`, `schema_compatible` (schema known to this build), `documents_complete`,
   `components_present`, `component_hashes_match`, `restored_schema_matches`,
   `row_counts_match_snapshot_window`, `representative_records_readable`,
   `restored_keys_decrypt_restored_data` (the restored key ring decrypts the restored MFA secrets).
   `covers_all_asset_classes` is a warning if the backup predates a newly managed asset class.
6. A successful drill counts toward probation and marks the backup **Fully verified**.
7. Inspect the recovered data if you wish (the isolated database and `.../extracted/documents|dataProtectionKeys`),
   then **Remove target** (asks for your password; drops only that drill's database and folder).

A failed drill removes whatever it created, records the stable failure code, audits it, and raises a
notification. Failure codes: `wrong_recovery_key`, `hash_mismatch`, `corrupt_or_tampered`, `not_a_backup`,
`incompatible_backup`, `documents_complete` / `components_present` / `component_hashes_match`,
`database_backup_damaged`, `restore_validation_failed`, `destination_unavailable`, `destination_full`, `io_error`.

### Starting the recovered application (full-state drill)

To prove the *application*, not just the data, runs on a recovered copy, start the API against the restored
pieces of a successful drill (the automated test `Full_state_drill_...` does exactly this):

```
ConnectionStrings__Alveara="Server=...;Database=AlveraRestore_<id>;..."
StorageRoot="<RestoreRoot>/<id>/extracted/documents"
DataProtectionKeysPath="<RestoreRoot>/<id>/extracted/dataProtectionKeys"
dotnet run --project src/Alveara.Api --no-build
```

Sign in as a restored account, open a restored document, and confirm a restored MFA-enabled account can
still complete its second factor. Record the date, the backup id, the drill id and the result in your
operations log.

## Disaster recovery: promoting a restored copy to production (deliberate and offline)

The application deliberately has **no** button or API that restores over live data. If the live system is
lost:

1. Stop the Alveara service. Keep the damaged database/files aside; do not delete them yet.
2. On the recovered server, run a restore **drill** (above) so you have a validated isolated copy
   (a fresh install needs the application deployed and an empty-ready SQL Server, plus the offline recovery key
   and passphrase - without them nothing can be recovered).
3. Point the service configuration at the validated restored database, documents folder and key-ring folder
   (or copy the restored files to the production locations and re-attach under the production database name using
   normal SQL Server procedures), then start the service. The fixed Data Protection application name
   (`Alveara.Dental`) makes the restored keys work from any folder or server.
4. If the backup's schema is older than the deployed application, start the application so its migrations run;
   a backup from a **newer** schema than the application is rejected as incompatible.
5. Sign in, check recent audit history and a few documents, then run a new backup and a new drill on the
   recovered system.

## Notifications

A notification is written for every backup success/failure, failed verification and failed drill. The default
local channel drops a text file into `Backup:NotificationDirectory` (monitor it with your usual tooling, e.g. a
file-watcher/email forwarder). **Send test notification** proves the channel works. A delivery failure is
recorded on its own (and shown) and **never** changes whether a backup is considered successful.

## Failure handling reference

| Situation | Behaviour |
|---|---|
| Destination missing / not writable / full | backup recorded **Failed** (`destination_unavailable` / `destination_full`), no partial file, notification; earlier success stays the "last success" and the page shows the new failure as the headline |
| Process dies mid-backup | scratch removed and run marked **Failed (`interrupted`)** on the next poll; the scheduled job retries (up to its attempt limit) into the same history row |
| Same scheduled slot polled twice / two servers | one job, one backup (idempotency key per slot) |
| Hash mismatch / corrupt file | verification **Failed**, notification, backup never reported as verified |
| Missing or wrong recovery key | refused with `wrong_recovery_key`; nothing restored; never reveals why beyond that |
| Document missing from the set / manifest mismatch | restore **Failed** before the database is touched; named in the result |
| Schema/format incompatible | rejected before restoring anything |
| Notification channel down | backup status unaffected; delivery failure recorded and visible |

## Security notes

Managing backups is administrator-only; the practice manager can view status and failures. Setting up the key,
verifying, restore drills and removing a target all require the caller's **current password** (through the same
lockout boundary as login). Every action is audited (`BackupCreated`, `BackupFailed`, `BackupFullyVerified`,
`RestoreDrillCompleted`, `RestoreTargetRemoved`, `RecoveryKeyConfigured`, ...). Key material, passphrases and
exception text are never logged, audited, stored, or returned after the one-time setup display.
