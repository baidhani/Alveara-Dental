# ALV-N004 R01 — Next Story Impact

## Next authorized item: GATE A (mandatory stop)

After this story is approved and closed, the next item is **GATE A — Foundation Ready**, not `STORY-003`. Evidence the gate can use from this story: row **A9** ("Encrypted full-state backup can be created, verified and restored for every persistent asset class that exists at this point") is addressed by the backup/restore drill evidence (`R01-evidence/ACCEPTANCE_EVIDENCE.md`, `DEMO_EVIDENCE.md`, the runbook). The gate is **not evaluated or marked here**. Gate A's A8 evidence (configuration) is satisfied by `ALV-N003`'s approved story evidence, and its backup of `ProviderProfiles.AvailabilityRevision` with the weekly windows is covered because the whole database is backed up.

## Interfaces later stories must use

- **A new persistent asset class must be added to backup.** Register an `IBackupAssetSource` (a `FileTreeAssetSource` for a folder; a new source type for anything else) in `Program.cs` **and** add the class name to `ManagedAssetClasses.All`. Until then, backups made earlier are visibly reported as not covering it. `ALV-N010` (documents) will store blobs under the existing `StorageRoot` that the `documents` source already captures; if it adds a document-metadata table it is inside the database backup. A story that adds any encrypted-at-rest data protected by a *different* key store must back that key store up too, or restored data will be unreadable (as the Data Protection key ring would have been).
- **Restore validation can grow.** Domain stories with integrity invariants (e.g. finalized clinical records, financial ledgers) should add post-restore checks in `BackupRestoreService.ValidateRestoredApplicationAsync` (hash-chain/total reconciliation after restore). Today's checks are schema, row counts within the snapshot window, representative records, and key/secret coherence.
- **Document hash records.** When the document module records a hash per document, the restore validation should additionally verify every restored document against its recorded hash (the blob storage's `VerifyIntegrityAsync` already supports it).
- **Idempotent external effects.** `ScheduledBackupJobHandler` is the reference for the documented external-effect contract: key the target record by the job's idempotency key, run in its own scope, throw on failure so the runner's retry/`MaxAttempts` applies.
- **Sensitive administrative actions.** `AccountService.ReauthenticateAsync` is the step-up primitive (current password, shared lockout, audited failure); use it for any action that exposes or uses recovery material.
- **Notification channel.** `IBackupNotifier` is the seam; email/Windows Event Log implementations plug in there. Delivery failure must stay independent of the thing being notified about.

## Assumptions / migrations / unresolved decisions

- **Data Protection application name is fixed to `Alveara.Dental`.** Any development database created before this change has MFA secrets protected under the old path-derived discriminator and needs MFA re-enrollment; recommend fixing the name before any real deployment (done here).
- Restore targets share the production SQL Server instance; staging/restore folders must be accessible to the SQL Server service account. A restore onto a different server is the offline runbook procedure (needs the offline recovery key file and passphrase).
- Retention/probation defaults (keep 7, 2 verifications, 30-day cadence, daily schedule) are policy choices an administrator can change; the hours-based schedule has no time-of-day anchor (a slot is an interval since the Unix epoch).
- Off-site/cloud backup targets and cloud DR are future extensions; a second destination would be another `BackupService` target, not a redesign.

## Prompt changes relevant to the next scheduled item

None required to the Execution Plan document itself. When Gate A is evaluated, the A9 criterion should be read as satisfied by an actual verified drill on the practice's real hardware (the repository evidence proves the mechanism; the first real-hardware drill is an operations task recorded in the operator's log).
