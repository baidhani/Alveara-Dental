# ALV-N004 R05 — Next Story Impact

Next authorized item after approval and closure: **GATE A** (mandatory stop), not `STORY-003`.

Later stories: any new archive-intrinsic post-restore check must use a code listed in `ArchiveDefects.Codes` (so it is recorded as a permanent defect) while environment-dependent failures must not be added to it; all writes of verification state must go through `BackupRecord.MarkVerificationFailed` or the guarded success paths. Everything from the R03/R04 notes still applies. No Execution Plan prompt change is required.
