# ALV-N002 R06 — Demo Evidence

R05 credibly demonstrated a real bridged LAN client and a non-bypassed certificate chain, but the reviewer found two real gaps: the *client's* own public internet was never blocked (only the server host's was), and the service-identity/ACL demonstration protected a stand-in folder/accounts on the client VM rather than the actual server process's real storage root and database. This attempt fixes both by adopting a genuinely correct topology: a dedicated **server VM** runs the real, unmodified application under a real least-privilege identity, and a separate, freshly cloned **client VM** exercises it — with that client's own internet blocked, not the server's.

## Topology for this attempt

- **Server VM** (`192.168.1.216`): the same VM used as the "client" in R05, repurposed as the actual server. Runs .NET 10, SQL Server 2025 Express, and the real published Alveara API — not a stand-in.
- **Client VM** (`192.168.1.109`): a fresh full clone of the server VM, taken *before* any server-role software was installed on it conceptually is not how it happened — practically, it inherited the server VM's already-trusted root CA (a convenience, not a security issue, since the private key was never on that image) and was then renamed and re-pointed to treat the *other* VM as the server. Its own SQL Server/app installation is irrelevant and unused for this test; it plays a pure LAN-client role.
- **Host**: completely uninvolved in this attempt's infrastructure — no accounts, ACLs, firewall rules, or certificates were touched on it.

## Part 1 — Real server: least-privilege SQL, real service account, real storage ACLs

1. Installed .NET 10 SDK and SQL Server 2025 Express (Mixed Mode authentication enabled) on the server VM.
2. Created `AlveraLanServerDemo` (a real database) and `alveara_app_login` — a SQL login scoped to only `db_datareader`+`db_datawriter` in that database, mirroring R02's `LeastPrivilegeAccessTests`. Verified via `sys.database_role_members`: exactly `db_datareader` and `db_datawriter`, no elevated role.
3. Published the real Alveara API (the same build validated in R04, including the client bundle in `wwwroot`) onto the server VM at `C:\AlveaaraServer`.
4. Created the dedicated `svc-alveara-api` local account and locked down the **real** storage root (`C:\AlveaaraServer\App_Data\blobs` — the actual path `LocalDiskBlobStorage` resolves to from `ContentRootPath`, not a stand-in) via NTFS ACLs: inheritance disabled, access re-granted only to `svc-alveara-api` and `Administrators`.
5. Cloned the repository onto the server VM and ran `dotnet ef database update` (as a Windows-authenticated administrator, which is distinct from the app's own least-privilege runtime connection) to apply the real EF Core migrations to `AlveraLanServerDemo`.
6. Ran the real `Alveara.Api.exe` under `svc-alveara-api`, with `ConnectionStrings__Alveara` pointed at `alveara_app_login` (not a Windows-integrated admin connection) and the LAN-hostname certificate configured for Kestrel. Confirmed locally on the server VM: `GET /api/systemstatus` → `200`, `database.reachable: true`, `backgroundRunner.status: "healthy"` — the real app, running under the real least-privilege identity, against the real ACL-protected storage path, genuinely working.
7. Negative check, same real path: `test-ordinary-user` (an unrelated local account) was denied access to the real storage root:
   ```
   ACCESS DENIED (expected): Access to the path 'C:\AlveaaraServer\App_Data\blobs' is denied.
   ```

## Part 2 — Real cross-VM LAN reachability, genuine TLS validation

1. Cloned the server VM to produce a genuinely separate client VM, renamed to avoid a duplicate-computer-name conflict on the LAN, confirmed its own bridged IP (`192.168.1.109`, distinct subnet-mate of the server's `192.168.1.216`).
2. Updated the client VM's `hosts` file to map `alveara-server.local` → `192.168.1.216` (the real server VM, not itself, not the host).
3. From the client VM, with **no certificate-check bypass of any kind**:
   ```
   Invoke-WebRequest -Uri "https://alveara-server.local:7180/api/systemstatus" -UseBasicParsing
   → 200, {"appVersion":"1.0.0.0","localServerReachable":true,"database":{"reachable":true},"backgroundRunner":{"status":"healthy",...}}
   Invoke-WebRequest -Uri "https://alveara-server.local:7180/" -UseBasicParsing            → 200
   Invoke-WebRequest -Uri "https://alveara-server.local:7180/system-status" -UseBasicParsing → 200
   Invoke-WebRequest -Uri "https://alveara-server.local:7180/api/health" -UseBasicParsing   → 200, {"status":"ok"}
   ```
   This is the real deployed server (least-privilege identity, real ACLs, real least-privilege SQL login), reached from a genuinely separate machine, over the real LAN, with the certificate chain validated by the OS with no override.

## Part 3 — LAN client cannot bypass the API to reach the database or storage directly

From the client VM, against the real server VM:
```
Test-NetConnection -ComputerName 192.168.1.216 -Port 1433
→ TcpTestSucceeded: False   (SQL Server's TCP endpoint is not reachable from the LAN client)

Test-Path "\\192.168.1.216\AlveaaraServer\App_Data\blobs"
→ False   (no file share exposes the storage root over the network)
```

The LAN client can only reach the application through the HTTP(S) API surface exercised in Part 2 — not the database, not the storage filesystem directly.

## Part 4 — Public internet disabled on the *client*, LAN access preserved (fixes N002-R05-01)

A Windows Firewall outbound rule (`-RemoteAddress Internet`) was applied on the **client VM** this time (not the server, and not the host — correcting the R05 finding that only the server's internet was ever blocked), paired with the same self-reverting Scheduled Task safety net used previously.

With the block active, confirmed on the client VM itself:
```
PS C:\Users\vboxuser> ping 8.8.8.8
Pinging 8.8.8.8 with 32 bytes of data:
General failure.
General failure.
General failure.
General failure.
Ping statistics for 8.8.8.8: Packets: Sent = 4, Received = 0, Lost = 4 (100% loss)
```

...while the **same client, at the same time**, successfully exercised the full shell/API surface over the LAN:
```
Invoke-WebRequest -Uri "https://alveara-server.local:7180/api/systemstatus" -UseBasicParsing → 200, database.reachable: true
Invoke-WebRequest -Uri "https://alveara-server.local:7180/"                -UseBasicParsing → 200
Invoke-WebRequest -Uri "https://alveara-server.local:7180/system-status"   -UseBasicParsing → 200
Invoke-WebRequest -Uri "https://alveara-server.local:7180/api/health"      -UseBasicParsing → 200, {"status":"ok"}
Invoke-WebRequest -Uri "https://alveara-server.local:7180/assets/<built-js>" -UseBasicParsing → 200
```

The firewall rule and its Scheduled Task both self-removed automatically at the scheduled time; public internet access on the client VM was confirmed restored afterward (`ping 8.8.8.8` succeeding again, rule absent from `Get-NetFirewallRule`).

## What this does not claim

- The exact production hostname, certificate authority, service-account provisioning automation, and SQL Server edition/licensing are `ALV-N013`/`ALV-N014`'s decisions — this proves the underlying mechanisms (least-privilege service identity, real ACL enforcement, real least-privilege SQL login, genuine LAN-only reachability with client-side offline operation) work end-to-end against the real application, not that this exact throwaway VM pair ships to production.
- Both VMs, their accounts, and the throwaway certificate/database are disposable demo artifacts, not part of this repository or any deployed environment.
