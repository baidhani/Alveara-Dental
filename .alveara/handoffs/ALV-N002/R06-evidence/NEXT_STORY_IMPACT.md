# ALV-N002 R06 — Next Story Impact

Carries forward R05's `NEXT_STORY_IMPACT.md` in full (repository path: `.alveara/handoffs/ALV-N002/R05-evidence/NEXT_STORY_IMPACT.md`). This file corrects it: the R05 runbook conflated client and server roles in a way the R05 review correctly rejected. The runbook below is the corrected, actually-validated version.

## The corrected infrastructure runbook `ALV-N013`/`ALV-N014` can adapt directly

1. **Server role**: install .NET runtime + SQL Server (Express or higher, Mixed Mode authentication if a SQL login is the chosen auth model) on the machine that will actually run the application. Publish the app there (`dotnet publish`, which — since ALV-N002 R04 — bundles the client into `wwwroot` automatically). Apply EF Core migrations using an elevated/Windows-authenticated connection (schema changes are an administrative operation, distinct from the app's own runtime connection). Create a dedicated non-administrative local account for the app's *runtime* identity, connect it to a SQL login scoped to only `db_datareader`+`db_datawriter`, and lock the real storage root's NTFS ACLs to that same account (plus `Administrators` for maintenance). Run the published executable under that dedicated account.
2. **Client role**: a genuinely separate machine (a second VM, in this exercise) with the server's certificate-issuing root CA trusted and a hosts-file (or real DNS) entry resolving the server's LAN hostname to its real IP. **The client's own internet access is what gets tested/disabled for the "no public internet" requirement — not the server's.** This was R05's core mistake; R06 corrects it.
3. **Negative checks that matter**: from the client, confirm the database's TCP port (e.g. 1433 for SQL Server) is not reachable, and confirm no file share exposes the storage root — proving the client can only reach the application through its HTTP(S) API, not around it.
4. **The self-reverting firewall-block pattern** (`-RemoteAddress Internet` plus a Scheduled Task that removes the rule a few minutes later) works cleanly and should be applied to whichever machine is playing the client role in a given test, not assumed to be the server.

## Resolved from R05

- ~~Block the client's own internet~~ — done, on a machine dedicated to the client role.
- ~~Exercise the actual server process under a real least-privilege identity with real storage ACLs~~ — done, on a machine dedicated to the server role.
- ~~Prove the LAN client cannot reach the database/storage directly~~ — done (closed SQL port, no file share).

## Still `ALV-N013`/`ALV-N014`'s decision, not resolved here

- The exact production hostname, certificate authority/issuance pipeline, SQL Server edition/licensing, and service-account provisioning automation remain deployment decisions for those stories.
- The two demo VMs, their accounts, and the throwaway certificate/database are disposable artifacts, not part of any deployed environment or this repository.
