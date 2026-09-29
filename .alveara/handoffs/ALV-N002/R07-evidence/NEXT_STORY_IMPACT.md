# ALV-N002 R07 — Next Story Impact

Carries forward R06's `NEXT_STORY_IMPACT.md` in full (repository path: `.alveara/handoffs/ALV-N002/R06-evidence/NEXT_STORY_IMPACT.md`). This file adds the versioned tooling that now exists.

## New: a reusable deployment-verification harness

`ops/deployment-verification/Invoke-ServerVerification.ps1` and `Invoke-ClientVerification.ps1` are now permanent, versioned repository artifacts — not one-off manual commands. `ALV-N013` (installation) and `ALV-N014` (security/privacy readiness) can run these unmodified against the real production server and a real LAN client to produce the same durable, timestamped, machine-generated evidence this attempt used, rather than re-deriving the check list from scratch. Parameters are already factored out (service account name, storage root, SQL instance/database/login, server hostname/IP, ports) so the same scripts apply to a real deployment, not just this disposable VM pair.

## Unresolved from R06, still unresolved

- The exact production hostname, certificate authority/issuance pipeline, SQL Server edition/licensing, and service-account provisioning automation remain deployment decisions for `ALV-N013`/`ALV-N014`.
- Both demo VMs, their accounts, and the throwaway certificate/database are disposable artifacts, not part of any deployed environment or this repository.
