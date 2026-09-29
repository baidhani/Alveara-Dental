# ALV-N002 R07 — Changed Files

Implementation commits `967b1e8c16ebc7e986c4a5e7b3d00909641080d2` (`ALV-N002 R07: add versioned server/client deployment verification harness`) and `c780277` (encoding fix, same attempt):

| File | Change |
|---|---|
| `ops/deployment-verification/Invoke-ServerVerification.ps1` (new) | Captures real process identity (via CIM), SQL role membership, storage ACL, ordinary-account denial, and service-account write/read proof — each to a timestamped file plus a `SUMMARY.json` verdict |
| `ops/deployment-verification/Invoke-ClientVerification.ps1` (new) | Captures a baseline route sweep, direct SQL-port/storage-share bypass checks, and the public-internet isolation window (block, failing probe, simultaneous route re-sweep, clean auto-revert) — each to a timestamped file plus a `SUMMARY.json` verdict |
| `ops/deployment-verification/README.md` (new) | Documents both scripts' purpose and usage |

This evidence commit additionally adds the raw, machine-generated output from running both scripts against the real two-VM topology:

| Path | Content |
|---|---|
| `.alveara/handoffs/ALV-N002/R07-evidence/artifacts/server/*` | Server-side verification run output (5 result files + 1 summary) |
| `.alveara/handoffs/ALV-N002/R07-evidence/artifacts/client/*` | Client-side verification run output (5 result files + 1 summary) |

No file under `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, or `GPT Docs/` was touched (confirmed zero diff — see `PARENT_REGRESSION.md`).
