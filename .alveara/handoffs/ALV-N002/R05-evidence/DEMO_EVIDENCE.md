# ALV-N002 R05 — Demo Evidence

This attempt closes the two acceptance items R04 honestly left `NOT ESTABLISHED` (item 1: local server / LAN client / no public internet; item 7: LAN client uses the API rather than direct database/storage access) through real infrastructure, not code. A VirtualBox Windows VM was provisioned as a genuinely separate LAN client; a real (non-bypassed) certificate trust chain, a real firewall-level internet cutoff, and real OS-level least-privilege accounts with filesystem ACLs were all exercised live and are transcribed below. No secrets (passwords, thumbprints usable to impersonate anything outside this disposable demo) are reproduced verbatim here beyond what's needed to describe the mechanism.

## Part 1 — Bridged LAN client (closes the "genuinely separate client" gap)

The VirtualBox VM's network adapter was set to **Bridged** (not NAT), giving it a real, independent DHCP lease on the same physical LAN as the host.

- Host LAN IP: `192.168.1.180`
- VM LAN IP (bridged): `192.168.1.216` — same subnet, not a `10.0.2.x` NAT address or a `169.254.x.x` self-assigned fallback.
- Bidirectional `ping` succeeded once a Windows Firewall inbound rule for ICMPv4 Echo Requests was enabled on the host (`File and Printer Sharing (Echo Request - ICMPv4-In)`, previously disabled for the Private/Domain profiles by default).

This is the first genuinely separate physical/virtual LAN client used across all of ALV-N002's attempts — R01–R04 could only reach the API from the server's own network interface.

## Part 2 — Real LAN-hostname certificate trust, no bypass (closes the R03 `curl -k`/`curl --resolve localhost` objection)

1. A local root CA (`CN=Alveara Dental Internal Root CA`, self-signed, 5-year validity) was created on the host.
2. A leaf certificate (`CN=alveara-server.local`, 2-year validity, Server Authentication EKU) was issued, signed by that root CA.
3. The root CA's **public certificate only** (no private key) was copied to the VM via a VirtualBox shared folder and imported into the VM's `Cert:\LocalMachine\Root` store — trusted **only on the VM**, mirroring how an organization deploys an internal CA to its LAN clients. The host itself does **not** trust this root CA.
4. The VM's `hosts` file was edited to map `alveara-server.local` → `192.168.1.180` (the host's real LAN IP) — a real LAN-hostname resolution, not `localhost` redirected elsewhere (the specific pattern the R03 review rejected).
5. Kestrel was configured (via `Kestrel:Certificates:Default:Path`/`Password` environment variables, not committed to any config file) to serve the leaf certificate instead of the ASP.NET Core development certificate, bound to `0.0.0.0:7180`.
6. From the **host**, `curl --resolve alveara-server.local:7180:192.168.1.180 https://alveara-server.local:7180/api/health` (without `-k`) correctly **failed** with `SEC_E_UNTRUSTED_ROOT` — proof the host genuinely does not trust this chain, i.e. validation is real, not universally bypassed.
7. From the **VM**, with no certificate-check bypass of any kind:
   ```
   Invoke-WebRequest -Uri "https://alveara-server.local:7180/api/health" -UseBasicParsing
   → 200, {"status":"ok"}
   Invoke-WebRequest -Uri "https://alveara-server.local:7180/" -UseBasicParsing
   → 200, body contains the real shell (<title>Alveara Dental</title>, built asset references)
   Invoke-WebRequest -Uri "https://alveara-server.local:7180/system-status" -UseBasicParsing
   → 200 (SPA fallback resolves the client-side route)
   Invoke-WebRequest -Uri "https://alveara-server.local:7180/api/systemstatus" -UseBasicParsing
   → 200, {"appVersion":"1.0.0.0","localServerReachable":true,"database":{"reachable":false},"backgroundRunner":{...}}
   Invoke-WebRequest -Uri "https://alveara-server.local:7180/assets/index-LorW5yfl.js" -UseBasicParsing
   → 200 (real built static asset)
   ```
8. Independently verified via a raw TLS handshake from the host (`SslStream.AuthenticateAsClient`) that the certificate actually served is `Subject: CN=alveara-server.local`, `Issuer: CN=Alveara Dental Internal Root CA` — confirming the VM's successful requests validated the real intended chain, not something else.

## Part 3 — Public internet disabled on the server while LAN access is preserved (closes the "no public internet" requirement)

A dry run was performed first to prove the mechanism was safe and self-reverting before relying on it: a Windows Firewall outbound rule (`-RemoteAddress Internet`, a built-in keyword meaning "any address outside the local subnet") was added on the host, paired with a Scheduled Task that automatically removes the rule a few minutes later — a dead-man's-switch, since blocking the host's internet also disconnects the operator's remote tooling running on that host, which must not depend on being reachable to revert its own change.

With the block active and independently confirmed on the host itself:
```
PS C:\Users\firas> ping 8.8.8.8
Pinging 8.8.8.8 with 32 bytes of data:
General failure.
General failure.
General failure.
General failure.
```

...the VM, on the same LAN, simultaneously succeeded against the real shell:
```
Invoke-WebRequest -Uri "https://alveara-server.local:7180/api/health" -UseBasicParsing       → 200 {"status":"ok"}
Invoke-WebRequest -Uri "https://alveara-server.local:7180/" -UseBasicParsing                  → 200
Invoke-WebRequest -Uri "https://alveara-server.local:7180/system-status" -UseBasicParsing     → 200
Invoke-WebRequest -Uri "https://alveara-server.local:7180/api/systemstatus" -UseBasicParsing  → 200, real database/runner status
```

The firewall rule and its scheduled task both self-removed automatically as designed; public internet access on the host was confirmed restored afterward (`Test-Connection 8.8.8.8` succeeded again), and this was independently verified twice (once as a dry run, once as the real timed test).

## Part 4 — Least-privilege service identity and storage-root ACLs (closes item 7)

Two local Windows accounts were created (on the VM, to avoid modifying the operator's primary host): `svc-alveara-api` (the API's service identity) and `test-ordinary-user` (a stand-in for "any other local account"). A folder mirroring the storage-root layout (`...\AlveaaraStorageDemo\blobs`) had its NTFS ACLs reset (`SetAccessRuleProtection($true, $false)` to strip inheritance, all existing rules removed) and re-granted only to `svc-alveara-api` and `Administrators`.

Running a file write-then-read as `svc-alveara-api` (via `Start-Process -Credential`, since this is a disposable client VM without WinRM enabled):
```
SUCCESS: written by svc-alveara-api
```

Running a directory listing as `test-ordinary-user` against the same folder:
```
ACCESS DENIED (expected): Access to the path '...\AlveaaraStorageDemo\blobs' is denied.
```

This proves the access-control *mechanism* (NTFS ACLs scoped to a dedicated non-administrative account deny an ordinary local account, and Windows enforces this at the OS level regardless of which process touches the file) that `ALV-N013`'s actual Windows Service packaging will apply to the real `App_Data/blobs` folder in production.

## What this does not claim

- The exact production hostname, certificate authority, and service-account provisioning script are `ALV-N013`/`ALV-N014`'s decisions — this proves the *mechanism* works end-to-end, not that this specific throwaway CA/account setup ships to production.
- The VM and its accounts (`svc-alveara-api`, `test-ordinary-user`) are disposable demo artifacts on a virtual machine, not part of this repository or any deployed environment.
