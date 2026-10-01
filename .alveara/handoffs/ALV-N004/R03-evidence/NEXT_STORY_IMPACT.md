# ALV-N004 R03 — Next Story Impact

Next authorized item after approval and closure: **GATE A** (mandatory stop), not `STORY-003`. A9 evidence: drill evidence incl. history-free recovery, deployment invariants enforced on backup, restore and at runtime.

Later stories: new persistent stores register an `IBackupAssetSource` and `ManagedAssetClasses.All`; new deployment-owned settings needed to interpret data must be added to `DeploymentSettings` **and** to `DeploymentInvariantRecord` (migration) so backup, manifest, asset and restored database keep agreeing; domain endpoints outside `/api/health|systemstatus|auth|backup` are automatically covered by the verified-before-serve guard; add domain post-restore checks in `ValidateRestoredApplicationAsync`. No Execution Plan prompt change is required.
