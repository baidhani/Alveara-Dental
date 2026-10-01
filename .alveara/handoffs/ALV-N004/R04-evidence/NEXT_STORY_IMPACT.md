# ALV-N004 R04 — Next Story Impact

Next authorized item after approval and closure: **GATE A** (mandatory stop), not `STORY-003`. A9 evidence: drill evidence incl. history-free recovery, deployment invariants enforced on backup/restore/runtime, and trust earned only by restore drills.

Later stories: new post-restore integrity checks added to `ValidateRestoredApplicationAsync` should use `restore_validation_failed` (an archive-intrinsic code) when they detect an archive defect, so it is recorded against the backup permanently; failures that depend on the recovering environment must use a different code so they do not poison the backup (`BackupRestoreService.ArchiveDefectCodes`). Everything else from the R03 next-story notes still applies. No Execution Plan prompt change is required.
