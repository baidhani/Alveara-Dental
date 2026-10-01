# ALV-N004 R01 — Demo Evidence

## The visible workflow, real browser + real API + real SQL Server

Playwright test 10 ("backup & recovery: set up the recovery key, run a real backup, and restore it into an isolated target"; output in `artifacts/playwright-real-backend/run-output.txt`) — real Chromium against a real `Alveara.Api` process and a freshly migrated database, as the bootstrapped admin:

1. `/admin/backup` shows **Backup & recovery**, the warning **"No recovery key is set up"**, and **Run backup now disabled** (nothing can be backed up without a key).
2. **Set up recovery key**: passphrase + confirmation + current password → the key file contents appear **once** in a read-only box with the warning that this is the only time it is shown; the test reads it (as an administrator would download it), ticks "I have stored the recovery key file and its passphrase offline", presses Done → the key is **gone from the screen** and the warning banner disappears.
3. **Run backup now** → "Backup completed."; the history row reads **Manual / Succeeded / "File hash verified only"** (backed up ≠ proven restorable), **Contains: Database, Documents, Encryption keys**, and the coverage line says the last backup **includes every managed asset class**.
4. **Verify / restore…** opens the **restore wizard**, which states **"never overwrites your live data"**; the test supplies the recovery key text, passphrase and current password; **Check compatibility and recovery material** shows "The recovery key and passphrase match this backup."; after ticking the acknowledgement, **Restore into an isolated target** runs a real SQL Server `RESTORE` into a new database and the result reads **"Restore drill succeeded"**, names the isolated database `AlveraRestore_…`, and says **"Your live data was not touched"**.
5. After closing the wizard the history row now reads **"Fully verified (restorable)"**; **Remove target…** asks for the password and removes only that restored copy ("The isolated restore target was removed.").
6. The audit viewer shows `RecoveryKeyConfigured`, `BackupCreated`, `RestoreDrillCompleted` and `RestoreTargetRemoved`.

Test 11 (extended) confirms a Dentist-role account never sees the **Backup & Recovery** link and is shown permission-denied at `/admin/backup`.

## Full restore drill procedure and evidence

**Procedure:** `docs/operations/BACKUP_AND_RECOVERY.md` ("Restore drill procedure", "Starting the recovered application", "Disaster recovery: promoting a restored copy to production").

**Evidence the drill works end to end** (all automated, all against real SQL Server):

| What was drilled | Evidence |
|---|---|
| Backup → isolated restore → every post-restore check passes (`format_supported`, `schema_compatible`, `documents_complete`, `components_present`, `component_hashes_match`, `restored_schema_matches`, `row_counts_match_snapshot_window`, `representative_records_readable`, `restored_keys_decrypt_restored_data`, `covers_all_asset_classes`) | `BackupRestoreTests.A_restore_drill_restores_into_an_isolated_target…` |
| The seeded account and its **protected MFA secret** are present in the restored database; restored **documents are byte-identical**; the live database is untouched | same test |
| **The recovered application starts** against the restored database, documents and keys: logs a restored account in, serves restored audit history, serves restored documents with intact hashes, and decrypts a restored MFA secret with its own Data Protection | `BackupRestoreTests.Full_state_drill_the_recovered_application_starts_and_serves_the_restored_records_documents_and_keys` |
| The same drill performed by a real person in a real browser | Playwright test 10 above |
| Drill rejects: wrong/missing key, corrupt/forged file, tampered or missing document, future schema/format, damaged database media, interrupted restore, keys that cannot decrypt restored secrets — each leaving no half-restored database or folder | `BackupRestoreTests` (named in `ACCEPTANCE_EVIDENCE.md`) |

A drill performed on the operator's real data is, by design, an *operations* record rather than a repository artifact (it would contain real data): the runbook says to log the date, backup id, drill id and result.

## States demonstrated by component tests

Prominent **failure headline** (with the older success still reported); **probation** and **overdue verification** notices; **incomplete asset coverage** warning; permission-denied vs **view-only** role (no actions, no key/settings/wizard); inline settings validation, unsaved-change protection, stale-edit conflict banner; mapped, fixed-text failure reasons (never server exception text); the recovery key never persisted and cleared from the DOM; wizard blocks restore until recovery material matches and the isolated-target acknowledgement is ticked; accessibility (axe) clean.

## Full regression

Backend 343/343; frontend 156/156 (`tsc`/build/lint clean); real-browser 12/12.
