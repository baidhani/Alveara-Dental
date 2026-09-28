# ALV-N002 R04 — Next Story Impact

Carries forward R03's `NEXT_STORY_IMPACT.md` in full (repository path: `.alveara/handoffs/ALV-N002/R03-evidence/NEXT_STORY_IMPACT.md`). This file adds only what changed in R04.

## New facts the next stories can rely on

- **`dotnet publish` now produces a real, self-contained server output that includes the client.** `Alveara.Api.csproj`'s `BuildClientForPublish`/`CopyClientBuildToPublishOutput` targets run automatically on `Publish`; `ALV-N013`'s installation story can rely on `dotnet publish -c Release -o <target>` alone producing a deployable `wwwroot/` with the built shell, with no separate manual client-build step.
- **Framework/database logging is now suppressed at the category level, not patched per call site.** Any new component that logs through the standard `ILogger<T>` pipeline automatically inherits the PHI-safe suppression of `Microsoft.EntityFrameworkCore.*` and `Microsoft.AspNetCore.Diagnostics` categories configured in `appsettings.json`. A future story adding its own EF-heavy service does not need to re-solve this; it only needs to avoid explicitly re-enabling those categories.
- **`SafeExceptionHandlingMiddleware` is now the single unhandled-exception boundary for the whole request pipeline.** Any future controller/route that lets an exception propagate unhandled gets the same safe (type-name + correlation-id only) treatment automatically — no need to add per-route try/catch for this specific concern.

## Unresolved — infrastructure runbook now in progress

The remaining N002-R03-01 gap (a real LAN hostname with a certificate a genuinely separate client trusts, disabled public internet, and a real least-privilege OS service identity with storage-root ACLs) is being addressed with a VirtualBox Windows VM the user is provisioning. Planned approach (not yet executed, tracked for R05 or a dedicated follow-up):

1. **Network topology:** the VM's network adapter set to Bridged (not NAT), so it gets a real, independent IP on the same physical LAN as the host — not a virtualized/NATed address, which would not constitute a genuinely separate LAN client.
2. **Hostname + certificate trust (replacing the R03 `curl --resolve localhost:...` shortcut the reviewer correctly rejected):** create a local root CA (e.g. via `openssl` or PowerShell's `New-SelfSignedCertificate -CertStoreLocation Cert:\LocalMachine\Root`), issue a leaf certificate for a real chosen LAN hostname (e.g. `alveara-server.local`) signed by that root, install the root CA as trusted **on the VM client only** (mirroring how an organization's internal CA is deployed to LAN clients — a standard, legitimate pattern, not a validation bypass), map that hostname to the host's real LAN IP via the VM's `hosts` file (or a small local DNS entry), and configure Kestrel to serve HTTPS with the issued certificate instead of the ASP.NET Core dev certificate.
3. **Disabling public internet while preserving LAN reachability:** a Windows Firewall outbound rule on the host blocking non-RFC1918 destinations (or temporarily disabling the host's WAN-facing adapter) for the duration of the LAN walkthrough, reverted afterward; documented as an exact, reversible step sequence.
4. **Least-privilege service identity + storage-root ACLs:** create a dedicated local Windows service/virtual account, run the API process under that identity (e.g. `sc.exe create ... obj= <account>`), grant NTFS permissions on the storage root to only that account, and add a negative test: an ordinary (non-service) local user account on the host cannot read/write the storage root directory.

This plan is not yet executed — it is recorded here as the concrete next step so R05 (or a dedicated infrastructure session) can proceed directly rather than re-deriving it.

## Unresolved from R03, still unresolved

- Production same-origin deployment strategy (exact hostname, certificate provisioning pipeline, service-account creation script) remains `ALV-N013`'s decision — this story only proves the underlying mechanism (publish bundling, logging safety) works.
- DST-ambiguity-resolution UX and database-engine choice — unchanged, not reopened.
