# ALV-N004 R05 — Acceptance Evidence

| Review finding (R04) | Closing evidence |
|---|---|
| R04-01 hash check erases a known archive defect / bypasses the guard | `A_failed_consistency_drill_then_a_passing_hash_check_then_verify_only_keeps_the_defect`, `A_failed_consistency_drill_then_a_hash_failure_or_environment_error_then_weaker_successes_keeps_the_original_defect`, `A_direct_failed_drill_then_verify_only_persists_the_reason_in_durable_state_and_the_api` (durable `ArchiveDefectCode`, history status/code, API DTO, zero confidence, no restore proof); `A_wrong_key_or_a_server_mismatch_is_not_an_archive_defect_and_does_not_poison_a_good_backup`; migration backfill |

Retained: R04 restore-proof separation and ordering tests (contradictory archive → verify-only; verify-only → failed drill; failed drill → verify-only), valid-drill credit and media-damage revocation, missing/unreadable restored deployment record, manifest/asset/database agreement, mismatched-host backup refusal, verified-before-serve guard, history-free archive recovery, OpenPGP/GnuPG, legacy Data Protection compatibility, non-default time zone.

Original seven acceptance items re-asserted: the history keeps creation outcome, contents verification, restore proof and a permanent archive defect as separate facts, and no sequence of hash, verify-only, wrong-key, environment-error or drill operations can clear a recorded defect or earn credit for a defective backup. 7 of 7.
