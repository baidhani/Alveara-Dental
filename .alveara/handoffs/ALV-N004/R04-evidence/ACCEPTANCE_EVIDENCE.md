# ALV-N004 R04 — Acceptance Evidence

| Review finding (R03) | Closing evidence |
|---|---|
| R03-01 verify-only promotes a contradictory archive and earns confidence | `Verify_only_never_counts_toward_trust_so_an_unknown_contradiction_earns_no_recovery_credit`, `Verify_only_then_a_failed_consistency_drill_leaves_the_backup_failed_with_no_credit`, `A_failed_consistency_drill_then_verify_only_cannot_erase_the_failure_or_earn_credit` (reviewer reproduction), `A_valid_drill_earns_credit_and_a_later_verify_only_keeps_it_while_a_later_media_failure_removes_it`, `A_restored_database_with_a_missing_or_unreadable_deployment_record_fails_the_drill_and_earns_nothing`, `Status_reports_probation_trust_…` (verify-only never counts); frontend labels/wording tests |

Retained: manifest/asset/database agreement and mismatched-host backup refusal (R03), verified-before-serve guard (R03), history-free archive recovery incl. app start/sign-in/MFA, OpenPGP/GnuPG interop, legacy Data Protection compatibility, non-default time-zone recovery (R01/R02 findings).

Original seven acceptance items: re-asserted. In particular "history truthfully distinguishes success, failure and verification state": creation (success/failure), contents verification (`FullyVerified` without restore proof, labelled as such), restore proof (drill only) and permanent archive defects are separate, visible facts; a backup proven defective cannot earn or keep recovery credit through any order of checks. 7 of 7.
