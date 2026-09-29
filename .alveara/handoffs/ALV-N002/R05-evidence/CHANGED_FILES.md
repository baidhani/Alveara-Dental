# ALV-N002 R05 — Changed Files

Implementation commit `dae6250e57489199c82669427807be46c718b566` (`ALV-N002 R05: gitignore the runtime storage root`):

| File | Change |
|---|---|
| `.gitignore` | Added `App_Data/` — the blob storage root exercised during this attempt's least-privilege infrastructure proof must never be committed |

No other source file changed in this attempt. The substance of R05 is the real infrastructure exercise (VM, certificate trust, firewall, OS accounts/ACLs) documented in `DEMO_EVIDENCE.md`, not a code change.

No file under `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, or `GPT Docs/` was touched (confirmed zero diff — see `PARENT_REGRESSION.md`).
