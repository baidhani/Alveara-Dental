# ALV-N004 R02 — Acceptance Evidence

Original acceptance items (see R01 evidence for the full 7-item table) re-proven on the corrected implementation, plus the four review findings.

| Review finding | Closing evidence |
|---|---|
| R01-01 recovery needs lost history | `BackupRecoveryHardeningTests.A_retained_archive_is_discovered_preflighted_and_restored_with_no_history_and_the_recovered_app_logs_in_with_MFA_data_intact` (history deleted, only retained archive + external key/passphrase; app started; sign-in, MFA secret, documents), `An_archive_can_only_be_chosen_from_the_two_archive_folders_never_by_arbitrary_path` (6 cases), `A_file_that_is_not_a_backup_is_listed_as_unreadable_and_a_tampered_archive_fails_the_drill_without_leaving_a_target`; frontend `Disaster recovery from a retained archive` (2); Playwright test 10 restores from the listed archive |
| R01-02 custom crypto format | `BackupCrypto` is OpenPGP via BouncyCastle; `BackupCryptoTests` (22): round-trip sizes, 96 MiB streaming, GnuPG interop both directions, tamper/truncation/append/swap, wrong key/passphrase, public key cannot decrypt, retired format rejected, bounded-memory streaming |
| R01-03 MFA payload compatibility | `An_existing_installation_keeps_decrypting_its_enrolled_MFA_secrets_after_upgrade_and_recovery_onto_a_different_folder_needs_exactly_the_recorded_name`; Program.cs no longer forces a name; recovered-app tests decrypt a restored MFA secret; existing MFA/auth suites unchanged and passing |
| R01-04 deployment invariants | `A_recovered_application_resolves_practice_local_times_identically_only_with_the_recorded_time_zone_and_refuses_data_requests_otherwise`, `A_backup_made_under_a_non_default_time_zone_records_it_and_recovery_is_blocked_until_the_server_is_configured_with_it`, `A_backup_with_no_recorded_deployment_settings_is_not_recoverable`, `The_coverage_check_reports_a_backup_without_deployment_configuration_as_incomplete`, `The_monitor_records_the_settings_on_first_start_flags_a_later_mismatch_and_the_guard_refuses_domain_calls_until_fixed`; updated asset coverage tests (4 classes) |

Original items unchanged in substance: manual/scheduled encrypted backups (`BackupServiceTests`, `BackupSchedulerTests`, API and browser flows); truthful history/verification state; every persistent asset class (database, documents, Data Protection keys, deployment configuration) restored and checked; recovery material held externally; protected restore (isolated target only, step-up); local testable notifications; audit/permission/CSRF boundaries.

Acceptance: 7 of 7 re-asserted on the corrected implementation (the R01 "7/7" is superseded; see handoff corrections).
