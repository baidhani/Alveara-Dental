# ALV-N002 R05 — Next Story Impact

Carries forward R04's `NEXT_STORY_IMPACT.md` in full (repository path: `.alveara/handoffs/ALV-N002/R04-evidence/NEXT_STORY_IMPACT.md`). This file replaces its "planned, not yet executed" infrastructure runbook with the now-executed, proven version.

## The infrastructure runbook `ALV-N013`/`ALV-N014` can now adapt directly

Every step below was executed live in this attempt, not just planned:

1. **Networking**: VirtualBox VM network adapter set to Bridged (not NAT) — gets a real, independent LAN IP. `ALV-N013` should document identifying and binding the correct LAN-facing interface on the actual target server, plus enabling the inbound ICMPv4 firewall rule if LAN diagnostics (ping) need to work (off by default on the Private/Domain profiles).
2. **Certificate trust**: a local root CA + a leaf certificate for a real LAN hostname, signed by that root; the root's public certificate (never the private key) distributed to and trusted only on LAN clients; Kestrel configured via `Kestrel:Certificates:Default:Path`/`Password` (environment variables, never committed). `ALV-N014` should replace the throwaway root CA used here with either a proper internal CA or a real publicly-trusted certificate if the deployment model allows one, but the *mechanism* (client-side trust store import, hostname resolution to the server's real IP, Kestrel certificate configuration via environment/config rather than code) transfers directly.
3. **Disabling public internet while preserving LAN**: a Windows Firewall outbound rule using the `-RemoteAddress Internet` keyword (blocks anything outside the local subnet) is the cleanest mechanism found — no need to enumerate specific IP ranges. Paired with a self-reverting Scheduled Task as a safety net for any operator relying on that same machine's internet connectivity for remote tooling.
4. **Least-privilege service identity + storage ACLs**: a dedicated non-administrative local account, NTFS ACLs on the storage root with inheritance disabled and access explicitly re-granted only to that account (plus `Administrators` for management). `ALV-N013`'s actual Windows Service installation should run the service under an equivalent dedicated account (a virtual service account or a provisioned `gMSA`/local service account, per the target environment's policy) and apply the same ACL pattern to the real `App_Data/blobs` path.

## Unresolved from R04, now resolved

- ~~A genuinely separate physical LAN client machine~~ — done via a bridged VirtualBox VM.
- ~~A real LAN hostname with a certificate a genuinely separate client trusts~~ — done, and proven non-bypassed by the host's own failed validation attempt.
- ~~Disabling public internet while preserving LAN reachability~~ — done, proven simultaneously on both sides.
- ~~A real least-privilege OS service identity with storage-root ACLs~~ — done, including a genuine negative-access denial.

## Still `ALV-N013`/`ALV-N014`'s decision, not resolved here

- The exact production hostname, certificate authority/issuance pipeline (self-signed internal CA vs. a real CA), and service-account provisioning automation (script vs. manual vs. `gMSA`) are deployment decisions for those stories — this attempt proves the underlying mechanisms work, not which specific option ships.
- The demo VM, its accounts, and its throwaway certificate chain are disposable artifacts, not part of any deployed environment or this repository.
