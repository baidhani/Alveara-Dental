# ALV-N002 R02 — Demo Evidence

R01's demo evidence was accepted for the System Status page's happy-path rendering, but the reviewer found two real defects a demo alone couldn't have caught (startup-outage host crash, LAN-vs-loopback conflation). This attempt adds targeted real-process and real-network demonstrations for exactly those gaps.

## N002-R01-03: real process survives a database outage, with sustained truthful status

1. Started the actual built API (`dotnet run`) with `ConnectionStrings__Alveara` pointed at a nonexistent LocalDB database — reproducing the reviewer's exact scenario.
2. `curl /api/health` → `200` (this endpoint doesn't touch the database, unaffected either way).
3. `curl /api/systemstatus` → `200 {"database":{"reachable":false},...}`.
4. Waited several seconds (more than one 10s background-poll interval) and repeated the request: **same truthful degraded result**, not a one-off.
5. Checked the process log: the real `SqlException` (database not found) is logged, but **no** "Application is shutting down" message appears — the host is still alive, unlike the reviewer's R01 reproduction.
6. Stopped the process after verification.

This exact scenario is now also codified as `RealProcessDatabaseOutageTests.cs`, which launches a real separate OS process, asserts it never exits, and asserts the degraded status is truthful across two polls.

## N002-R01-05: real LAN + HTTPS reachability, not loopback

1. Found this machine's actual LAN adapter IP (`192.168.1.180`, distinct from `127.0.0.1`).
2. Started the API bound to `0.0.0.0` on both HTTP (`:5072`) and HTTPS (`:7180`).
3. `curl http://192.168.1.180:5072/api/health` → `200 {"status":"ok"}` — reached over the real network interface, not loopback.
4. `curl -k https://192.168.1.180:7180/api/health` → `200`, with `curl -v` output showing a genuine TLS handshake (`schannel: SSL/TLS connection renegotiated`) against the ASP.NET Core development certificate.
5. Stopped the process after verification.

## N002-R01-05: least-privilege database access, demonstrated not asserted

`LeastPrivilegeAccessTests.cs` creates a real SQL Server login scoped to only `db_datareader`+`db_datawriter`, connects the actual `AlveraDbContext` through it, performs a real insert and read, then — using the same restricted connection — attempts `CREATE TABLE` and `BACKUP DATABASE` and confirms SQL Server itself rejects both. This is a live demonstration of the access boundary, not a configuration claim.

## Why this counts as "demonstrated"

Each of these three reproductions uses the real compiled application (or a real SQL Server login) against a real database engine (LocalDB) — not a mock, not an in-process test server standing in for the real hosting model. The startup-outage and LAN/HTTPS reproductions specifically target the two ways the R01 demo evidence was found insufficient: an in-process `WebApplicationFactory` cannot reproduce a host-level crash, and a `localhost` request cannot prove non-loopback reachability.
