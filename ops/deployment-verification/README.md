# ALV-N002 Deployment Verification Harness

Two PowerShell scripts that capture durable, timestamped, machine-generated evidence for ALV-N002's LAN/security acceptance items (a LAN client can use the production shell with no public internet; that client uses the API rather than reaching the database/storage directly). They exist because narrative descriptions of manually-run commands are not acceptable evidence — see the ALV-N002 R06 independent review, finding N002-R06-02.

## `Invoke-ServerVerification.ps1`

Run **on the machine actually hosting the deployed API**, elevated. Captures, to timestamped files in `-OutputDir`:

1. The real process identity of the running API (via CIM, not `Get-Process` alone, since only CIM exposes the owning account) — proves it runs under the intended service account.
2. SQL role membership for the configured login against `sys.database_role_members` — proves it is scoped to exactly `db_datareader`+`db_datawriter`.
3. The real storage root's NTFS ACL (`icacls`) — proves it is not broadly readable.
4. A negative check: an unrelated local account attempting to list the real storage root — expected to fail.
5. A positive check: the service account writing to and reading back from the real storage root — expected to succeed (this step prints the exact command to run as that identity if the script itself isn't already running under it).

Writes a `SUMMARY.json` with a PASS/FAIL verdict per check.

## `Invoke-ClientVerification.ps1`

Run **on a genuinely separate LAN client machine**, elevated. Captures:

1. A baseline route sweep (`/`, `/system-status`, `/api/health`, `/api/systemstatus`) over HTTPS with real certificate validation (no bypass flags).
2. Direct-bypass checks: a TCP probe against the server's SQL port, and a UNC-path check against a guessed storage-root file share — both expected to fail.
3. The isolation window: blocks this client's own public internet via a Windows Firewall rule (paired with a self-reverting Scheduled Task), captures an explicit failing public-internet probe, then re-runs the route sweep **during the same block** to prove LAN access and public-internet unavailability held simultaneously — not as two claims stitched together from different points in time.
4. Confirmation that the block auto-reverted cleanly (rule removed, public internet restored) before the script exits.

Writes a `SUMMARY.json` with a PASS/FAIL verdict per check.

## Why two scripts, not one

The server and client roles must run on genuinely different machines for the acceptance items to mean anything — a single script can't meaningfully play both roles. Each script owns exactly the checks reachable from its own vantage point.

## Evidence handling

Every run's output files (timestamped `.txt` and one `SUMMARY.json` per script) are collected into the relevant `.alveara/handoffs/ALV-N002/<attempt>-evidence/` directory as raw, unedited captures — not summarized into prose. No script prints or logs any password; account credentials are always passed in as `SecureString`/`PSCredential` objects at the call site, never hardcoded here.
