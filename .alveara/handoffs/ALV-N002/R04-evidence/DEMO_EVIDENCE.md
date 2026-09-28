# ALV-N002 R04 — Demo Evidence

R03's shell/LAN demo was accepted for what it covered but the reviewer found the clean `dotnet publish` output didn't actually contain the shell, and the `curl --resolve localhost:...` trick resolved `localhost` to another machine rather than proving a real LAN hostname trust path. This attempt fixes the publish-output defect and reproduces the reviewer's exact clean-publish check with the opposite result; it does not attempt a new LAN-hostname/cert demo, since that genuinely requires the second-machine infrastructure covered in `NEXT_STORY_IMPACT.md`.

## N002-R03-01 (partial): clean publish output now serves the shell

1. `dotnet publish Alveara.Api/Alveara.Api.csproj -c Release -o <fresh temp directory>` — the new MSBuild targets ran `npm ci && npm run build` for the client and copied `dist/` into `<publish dir>/wwwroot/`.
2. Ran the published executable directly, with no repository context and a fresh LocalDB connection string via environment variable — reproducing exactly how the reviewer's independent check was run.
3. `/api/health` → `200` (unchanged from R03, this endpoint never depended on the client bundle).
4. `/` → `200` with `<title>Alveara Dental</title>` in the body (R03: `404`).
5. `/system-status` → `200`, the SPA fallback resolving a client-side route from a direct request against the published output (R03: `404`).
6. Stopped the process after verification.

**Not attempted this round, and explicitly not claimed as demonstrated:** a real LAN hostname with a certificate a genuinely separate client trusts, disabling public internet while preserving LAN reachability, and a provisioned least-privilege service identity with storage-root ACLs. These need the second-machine (VirtualBox VM) infrastructure the user is setting up; see `NEXT_STORY_IMPACT.md` for the plan.

## N002-R03-02: framework-level PHI-safe logging, reproduced then disproved

`FrameworkLoggingPhiSafetyTests.A_synthetic_patient_like_value_in_a_real_unique_constraint_violation_never_appears_in_any_captured_log_across_any_category` reproduces the reviewer's exact scenario: a real `WebApplicationFactory` host with the actual configured logging pipeline (appsettings.json's category filters included, not bypassed), a genuine SQL Server duplicate-key violation via `UserAccount.Username`'s unique index, with a synthetic patient-like value (`SyntheticPatientAliceExample-<guid>`), and a capturing `ILoggerProvider` recording every log record across every category. The synthetic value is absent from all captured output — the opposite of the reviewer's R03 finding (which found it present via EF's own `Database.Command` logging).

## Why this counts as "demonstrated"

Both reproductions use the real compiled application (a genuine `dotnet publish` output, and a real ASP.NET Core host with its actual configured logging pipeline) against real infrastructure (LocalDB) — not a mock and not a bypass of the specific mechanism the reviewer flagged. What remains undemonstrated (real LAN hostname/cert trust, disabled public internet, real service-account ACLs) is stated plainly, not glossed over — see `ACCEPTANCE_EVIDENCE.md`.
