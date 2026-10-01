# ALV-N004 R01 — Acceptance Evidence

## Acceptance / stop condition

| # | Acceptance item | Proving evidence |
|---|---|---|
| 1 | Manual and scheduled encrypted backups work | `BackupServiceTests.A_manual_backup_creates_one_encrypted_file…` and `…genuinely_encrypted…` (real SQL Server `BACKUP`, AES-256-GCM, no plaintext markers); `BackupSchedulerTests.A_scheduled_job_produces_exactly_one_encrypted_backup…` (through the real durable job runner); `BackupApiTests.Manual_backup_verification_and_restore_drill_work_end_to_end…`; Playwright flow (real backup via the UI) |
| 2 | Backup history truthfully distinguishes success, failure and verification state | separate `BackupStatus` / `BackupVerificationStatus` / failure code on every row: a fresh backup is `HashVerified` ("File hash verified only"), never "verified"; `Hash_verification_confirms_an_intact_file_and_distinguishes_a_changed_or_missing_one`; `Status_reports_probation_trust_verification_age…`; a failed latest attempt is the headline while the older success stays "last success" (`An_unavailable_destination_fails_visibly…`); frontend `history keeps success, failure and verification as separate truths` and `makes a failed latest attempt the headline` |
| 3 | A test backup/restore covers every persistent asset class that exists at this point | asset classes = `database`, `documents`, `dataProtectionKeys` (registry `ManagedAssetClasses`). `BackupRestoreTests.A_restore_drill_restores_into_an_isolated_target_validates_every_asset_class…` restores all three and checks them (DB records incl. the protected MFA secret; blobs byte-identical via `IBlobStorage.VerifyIntegrityAsync`; key ring decrypts the restored secret); `Full_state_drill_the_recovered_application_starts…`; the status/API report whether a backup covers all classes (`covers_all_asset_classes`, `LastSuccessCoversAllAssetClasses`, older-backup test) |
| 4 | Missing/wrong recovery material is handled safely and visibly | `BackupCryptoTests` (wrong passphrase, foreign key, garbage → `wrong_recovery_key`, no oracle beyond that); `Preflight_flags_missing_wrong_and_foreign_recovery_material…`; `A_drill_with_the_wrong_recovery_material_fails_with_its_own_code_creates_no_target_and_notifies`; `Full_verification_with_missing_or_wrong_recovery_material_fails_visibly_and_safely` (never counts toward confidence); API `A_drill_with_the_wrong_key_returns_a_safe_failure_and_never_echoes_the_key_material`; frontend wrong-key preflight blocks both actions |
| 5 | A verified restore drill can start the recovered application and validate representative records/assets | `Full_state_drill_the_recovered_application_starts_and_serves_the_restored_records_documents_and_keys`: boots the real `Program` on the restored database/documents/keys, logs in a restored account, reads restored audit history, verifies restored blobs, and decrypts a restored MFA secret with the app's own Data Protection; plus the in-service validation checks and the real-browser restore; procedure in `docs/operations/BACKUP_AND_RECOVERY.md` |
| 6 | Backup success/failure is visible and notifications are testable | status panel / failure headline / history; `Notifications_are_testable_and_the_default_local_channel_writes_a_drop_file`; `A_notification_delivery_failure_does_not_mark_a_successful_backup_failed…`; API `The_test_notification_endpoint…`; frontend notification list, test button, delivery-failure display |
| 7 | No required restore secret exists only inside the encrypted backup | `No_secret_needed_to_restore_exists_only_inside_the_backup_or_anywhere_the_server_stores_it` (server stores only the public key; private key absent from DB/audit/history **and from every file inside the decrypted backup**); `The_server_side_public_key_alone_can_never_decrypt`; API shows the key once with `no-store` and never again; the key is the administrator's offline custody (runbook) |

## Required tests (per the prompt)

| Required test | Where |
|---|---|
| Scheduled/manual backup tests | `BackupServiceTests`, `BackupSchedulerTests`, `BackupApiTests` |
| Background-job restart/idempotency test | `BackupSchedulerTests` (slot idempotency, job stuck InProgress after restart, retry-in-place, capped failure), `BackupServiceTests` (same-key idempotency, interrupted run) |
| Encryption/authentication/recovery-material tests | `BackupCryptoTests`, recovery-key tests in `BackupServiceTests`/`BackupApiTests` |
| Database-consistent snapshot/restore test | `BackupRestoreTests` (real `BACKUP`/`RESTORE`, row counts within the snapshot window, representative records), existing `BackupSnapshotTests` |
| Corrupt/incompatible backup tests | `BackupRestoreTests` (hash mismatch, forged hash caught by GCM, tampered component, damaged DB media, future schema/format, escaping manifest path) |
| Full-state restore drill | `Full_state_drill_…` + Playwright real flow + runbook procedure |
| Missing-document/blob recovery test | `A_document_missing_from_the_backup_set_fails_the_restore_and_names_the_problem` |
| Authorization/audit tests | `BackupApiTests` (role matrix, CSRF, step-up, lockout, audit events end to end) |

## Failure paths named by the prompt

| Failure path | Handling / evidence |
|---|---|
| Backup destination unavailable/full | `destination_unavailable` / `destination_full` (classified from the real I/O error), no partial file, history + notification; tests above |
| Backup interrupted mid-run | `.partial` + atomic publish, scratch cleanup, `RecoverInterruptedAsync` → Failed(`interrupted`), retried into the same row |
| Verification/hash mismatch | `hash_mismatch` / `corrupt_or_tampered`, never reported as verified, notification |
| Missing/wrong recovery material | see item 4 |
| Incompatible schema/application version | `incompatible_backup` before any restore |
| Document/blob missing from backup set | `documents_complete` failure before the database is touched |
| Restore validation fails | `restore_validation_failed` with the failing check (e.g. restored keys cannot decrypt restored secrets); target removed |
| Notification delivery fails without marking a successful backup failed | proven, recorded separately and shown |

## Security, audit and integrity requirements

| Requirement | Evidence |
|---|---|
| Strongly authorized and audited | `ManageBackups` (Admin only) + current-password step-up with lockout; audit events for every action (`BackupApiTests` end-to-end audit assertions, `SensitiveActionStepUpFailed`) |
| Keys/secrets never committed/logged or only inside the backup | see item 7; no key material in audit details (asserted); exception text never returned or stored (`Low_level_failures_map_to_stable_codes_and_fixed_messages`) |
| Reviewed platform/library cryptography | `AesGcm`, `RSA` (OAEP-SHA256), `ExportEncryptedPkcs8PrivateKeyPem`/`ImportFromEncryptedPem`, `RandomNumberGenerator`; see the framing disclosure in `R01.md` |
| Restore does not overwrite the active data set without an explicit protected workflow | no overwrite code path or endpoint (`There_is_no_endpoint_that_restores_over_the_live_data`); `The_restore_provider_can_never_overwrite_an_existing_database_including_the_live_one`; promotion is the deliberate offline runbook procedure |

## UI/UX deliverables

| Deliverable | Evidence |
|---|---|
| Backup & Recovery page with history, storage target, schedule/retention, last success/failure, verification state | `BackupRecoveryPage` + frontend tests + `DEMO_EVIDENCE.md` |
| Restore wizard with explicit target, compatibility and recovery-material checks | `RestoreWizard` (steps, never-overwrite statement, isolated target named, preflight checks) + tests |
| Prominent failure state without exposing encryption material | failure headline alert; mapped fixed messages only; recovery key shown once and cleared (tests assert it is gone from the DOM and storage) |
| Whether a backup set includes all currently managed asset classes | asset-coverage line + history "Contains" column + API `lastSuccessCoversAllAssetClasses` |
