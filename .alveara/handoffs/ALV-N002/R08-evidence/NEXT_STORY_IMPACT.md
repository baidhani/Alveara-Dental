# ALV-N002 R08 — Next Story Impact

Carries forward R07's `NEXT_STORY_IMPACT.md` in full (repository path: `.alveara/handoffs/ALV-N002/R07-evidence/NEXT_STORY_IMPACT.md`). This file adds one new, reusable technique.

## New: SQL session-to-process correlation, a reusable verification pattern

`Invoke-ServerVerification.ps1`'s SQL session correlation check (querying `sys.dm_exec_sessions` for a live session under the target login and matching its `host_process_id` against the running application's own PID) is a general technique for proving "this specific running process is the one using this specific database credential" — not just that the credential exists with the right permissions. `ALV-N013`/`ALV-N014`, or any future story needing to prove a deployed process's actual database identity, can reuse this pattern directly.

## Unresolved from R07, still unresolved

- The exact production hostname, certificate authority/issuance pipeline, SQL Server edition/licensing, and service-account provisioning automation remain deployment decisions for `ALV-N013`/`ALV-N014`.
- Both demo VMs, their accounts, and the throwaway certificate/database are disposable artifacts, not part of any deployed environment or this repository.
