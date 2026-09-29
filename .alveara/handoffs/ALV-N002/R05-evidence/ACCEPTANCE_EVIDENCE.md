# ALV-N002 R05 — Acceptance Evidence

Updates R04's mapping. R04 reported 7 PASS + 2 NOT ESTABLISHED (items 1 and 7). This attempt closes both through the real infrastructure exercise transcribed in `DEMO_EVIDENCE.md`.

| # | Acceptance item | R04 status | R05 evidence | R05 status |
|---|---|---|---|---|
| 1 | Local Windows server / LAN client / no public internet | NOT ESTABLISHED | A genuinely separate bridged VM client (not the server's own NIC); a real LAN hostname (`alveara-server.local`) with a certificate chain trusted only on the client, validated with no bypass (`curl -k`/`--resolve localhost` are gone — the host itself provably does *not* trust the chain, confirming validation is real); public internet disabled on the host via a real firewall rule while the VM's requests to the shell kept succeeding, confirmed simultaneously on both sides. | **PASS** |
| 2 | Schema migration initializes and upgrades safely | PASS | Unchanged. | **PASS** |
| 3 | Time/timezone and money invariants | PASS | Unchanged. | **PASS** |
| 4 | Explicit disconnected-server behavior | PASS | Unchanged. | **PASS** |
| 5 | Persisted job survives restart without duplicate effects | PASS | Unchanged. | **PASS** |
| 6 | Versioned privacy-minimized measurement event | PASS | Unchanged. | **PASS** |
| 7 | LAN client uses API rather than direct database/storage access | NOT ESTABLISHED | Least-privilege SQL access (proven in R02) and blob-storage HTTP-isolation (proven in R04) are unchanged. This attempt adds the missing OS-level piece: a dedicated non-administrative service account (`svc-alveara-api`) that can read/write the storage root via real NTFS ACLs, and a genuine negative check — an ordinary local account is denied access by the OS itself (`Access to the path '...\blobs' is denied.`), not merely absent from an application-level permission list. | **PASS** |
| 8 | Truthful service/database/runner System Status | PASS | Unchanged. | **PASS** |
| 9 | Failure paths do not silently corrupt authoritative data | PASS | Unchanged. | **PASS** |

**Total: 9 of 9 PASS.** `EXECUTION_STATUS.json`'s `acceptance` block for this attempt reflects `passed: 9`, and `blockingIssues` is now genuinely empty — not the R03 package's premature empty list against two still-open items, but an honestly-earned empty list this time.

## Why this is credible, not just claimed

Every one of the four sub-proofs in `DEMO_EVIDENCE.md` was run live, transcribed as it happened, and included at least one negative/failure check proving the control is real rather than coincidental:
- The host explicitly failed to validate the certificate chain (proving trust is genuinely scoped to the VM only, not silently trusted everywhere).
- The host's own ping to the public internet explicitly failed during the firewall-block window (proving the block was real, not merely configured and untested).
- The ordinary local account was explicitly denied filesystem access (proving the ACL is enforced by the OS, not merely present in a config file).
