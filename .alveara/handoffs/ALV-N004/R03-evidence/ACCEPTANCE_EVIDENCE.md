# ALV-N004 R03 — Acceptance Evidence

| Review finding (R02) | Closing evidence |
|---|---|
| R02-01 domain requests allowed before verification | `Domain_requests_wait_until_deployment_invariants_are_verified_and_are_refused_while_unverified`, `A_request_that_arrives_before_the_first_check_triggers_it_and_is_served_only_when_it_verifies`, `A_failing_or_unreachable_initial_check_keeps_domain_requests_refused_instead_of_letting_them_through` (incl. real monitor vs unreachable database); `The_monitor_records_the_settings_on_first_start_flags_a_later_mismatch_and_the_guard_refuses_domain_calls_until_fixed`; recovered-app tests |
| R02-02 contradictory deployment settings in a successful drill | `A_host_configured_against_its_databases_recorded_settings_cannot_produce_a_backup_at_all`, `A_contradictory_archive_whose_manifest_disagrees_with_its_captured_database_never_reports_success`, `A_deployment_asset_that_contradicts_the_manifest_is_rejected_before_anything_is_restored`, `A_legacy_database_without_a_deployment_record_gets_one_from_the_running_settings_and_the_backup_then_recovers_consistently` |

Retained regressions (R01/R02 findings): history-free archive recovery incl. app start/sign-in/MFA (`A_retained_archive_is_discovered_…`), OpenPGP/GnuPG interop and tamper tests (`BackupCryptoTests`), legacy Data Protection compatibility and recovery onto another folder, non-default time-zone recovery (`A_recovered_application_resolves_practice_local_times_identically…`).

Original seven acceptance items: re-asserted on the corrected implementation (manual and scheduled encrypted backups; truthful history/verification state - a contradictory backup can no longer be FullyVerified; every persistent asset class incl. deployment configuration with manifest/asset/database agreement; missing/wrong recovery material; recovered application start and validation; visible failures and testable notifications; no restore secret only inside a backup). 7 of 7.
