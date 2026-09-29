# ALV-N002 R08 — Demo Evidence

R07's client-side evidence was fully accepted. Three findings remained, all on the server side: the declared implementation SHA didn't match the tree that actually produced the evidence (N002-R07-01); the service-account write/read verdict had been manually patched into `SUMMARY.json` after the fact, with a timestamp that proved it (N002-R07-02); and SQL role-membership evidence proved a login's roles but not that the running API's actual database session used it (N002-R07-03). This attempt reruns the corrected, single-pass server harness — no manual step anywhere — and its raw output is committed unedited at `artifacts/server/`.

## N002-R07-01 — implementation identity, fixed by construction

R08 has exactly one implementation commit, `02a8c98533cc604fce4ac9f27c46f119f5b9065f`, containing every fix. There is no follow-up commit whose tree diverges from what's recorded in the ledger/manifest/ZIP filename — the exact ambiguity the R07 review flagged cannot recur here because there is nothing to be ambiguous between.

## N002-R07-02 — the script performs and verifies every check itself, in one run

`artifacts/server/20260929-065256-SUMMARY.json`'s `capturedAtUtc` is `2026-09-29T11:52:58.8550164Z`. Every individual result file it reports precedes that timestamp:

| Check | File | `CapturedAtUtc` / embedded timestamp | Verdict |
|---|---|---|---|
| Process identity | `20260929-065256-process-identity.txt` | `2026-09-29T11:52:56.6232108Z` | `PASS: running as svc-alveara-api (PID 7172)` |
| Forced database-backed request | `20260929-065256-systemstatus-response.txt` | `2026-09-29T11:52:56.7011029Z` | `PASS: HTTP 200, database.reachable=true` — full response body retained: `{"appVersion":"1.0.0.0","localServerReachable":true,"database":{"reachable":true},"backgroundRunner":{"status":"healthy",...}}` |
| SQL role membership | `20260929-065256-sql-role-membership.txt` | `2026-09-29T11:52:56.7354216Z` | `PASS: exactly db_datareader + db_datawriter` |
| Storage ACL | `20260929-065256-storage-acl.txt` | `2026-09-29T11:52:56.7497121Z` | `PASS: only svc-alveara-api (+ Administrators) present` |
| Ordinary-account denial | `20260929-065256-ordinary-denial-result.txt` | `2026-09-29T06:52:57.3884621-05:00` (`11:52:57Z`) | `ACCESS DENIED (expected): running as WINDOWS-11-VM3\test-ordinary-user, ...` — the child process's **own** `[Security.Principal.WindowsIdentity]::GetCurrent().Name` is embedded in the result, not an interpolated string, so the file is self-authenticating about who actually ran it. |
| Service-account write/read | `20260929-065256-svc-write-result.txt` | `2026-09-29T06:52:58.3116030-05:00` (`11:52:58Z`) | `SUCCESS: running as WINDOWS-11-VM3\svc-alveara-api, readback='written by WINDOWS-11-VM3\svc-alveara-api at ...'` — same self-authentication, and this is the **last** individual check before the summary, so the summary timestamp (`11:52:58.855Z`) genuinely follows it (`11:52:58.312Z`), unlike R07's impossible ordering. |

Both `Start-Process -Credential ... -Wait` calls run to completion inside the script itself; nothing was typed, patched, or completed by a human after the script exited.

## N002-R07-03 — the running API's actual SQL session, correlated to its own PID

`artifacts/server/20260929-065256-sql-session-correlation.txt`, captured immediately after forcing a real request to `/api/systemstatus`:

```
session_id login_name        host_name      program_name                                      host_process_id status
---------- ----------        ---------      ------------                                      --------------- ------
        51 alveara_app_login WINDOWS-11-VM3 EFCore/10.0.12 (Microsoft Windows 10.0.26200 X64)            7172 sleeping
```

`host_process_id=7172` matches `20260929-065256-process-identity.txt`'s `PID=7172 Name=Alveara.Api.exe Owner=WINDOWS-11-VM3\svc-alveara-api` exactly — this is SQL Server's own record of which client process opened the session, not a claim made by this script. `program_name` further confirms it's the real EF Core client, not an ad-hoc `sqlcmd` connection. Role membership (`sys.database_role_members`, separately) and live session identity (`sys.dm_exec_sessions`, this check) together prove the running API's actual database traffic goes through the tested least-privilege login — not merely that such a login exists somewhere with the right roles.

## Client-side evidence (unchanged, reused from R07)

`artifacts/client/` — see `.alveara/handoffs/ALV-N002/R07-evidence/DEMO_EVIDENCE.md` for the full write-up; these files are byte-identical copies, not regenerated, since the R07 review found no defect on the client side.

## What this does not claim

- The exact production hostname, certificate authority, service-account provisioning automation, and SQL Server edition/licensing remain `ALV-N013`/`ALV-N014`'s decisions.
- Both VMs, their accounts, and the throwaway certificate/database are disposable demo artifacts, not part of this repository's runtime or any deployed environment.
