# ALV-N002 R04 — Git State

## Branch

`main`

## Implementation SHA

`b0ff9a4348231603f432a4f596f97763b8de23a8`

## Evidence SHA

Not yet known at the time this file was committed — this file is part of the evidence commit itself (Git self-reference prohibition). Per `HANDOFF_SCHEMA.md` Section 3, the committed `EXECUTION_STATUS.json`'s `evidenceCommit` field is likewise left `null` for this attempt — no follow-up commit is made to backfill it, correcting the R03 attempt's protocol failure (N002-R03-03), where a third commit changed `EXECUTION_STATUS.json` after the evidence commit, causing the packaged ZIP's `EXECUTION_STATUS.json` to no longer byte-match the evidence SHA declared in its own `MANIFEST.json`. The generated ZIP's `MANIFEST.json` and its own **separately generated** copy of `evidence/GIT_STATE.md` (not this committed file) record both SHAs once the evidence commit exists.

## Working-tree state

Clean immediately before and after the R04 implementation commit.

## Relevant commits (oldest → newest)

```
7d312b4 ALV-N002: Core Architecture, Local Deployment, Data Invariants, and Durable Background Work (R01 implementation)
cb79374 ALV-N002: record engineering handoff                     (R01 evidence)
848d0fc ALV-N002: correct execution (R02) — atomic job claims, transactional effects, sustained degraded status, strict measurement validation, verified LAN/HTTPS/least-privilege (R02 implementation)
b4c55ce ALV-N002: record R02 engineering handoff                  (R02 evidence)
951b425 ALV-N002 R03: fix all four R02 review findings            (R03 implementation)
1b96e48 ALV-N002: record R03 handoff, R02 review decision, ledger (R03 evidence)
dc37ac7 ALV-N002: record R03 evidence commit SHA in ledger        (R03 protocol violation — flagged by N002-R03-03; not repeated for R04)
b0ff9a4 ALV-N002 R04: fix publish bundling and framework PHI-safe logging (R04 implementation)
```
