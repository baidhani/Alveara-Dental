# Milestones: week of 2026-10-07

Written 2026-10-07 after TESTSPEED-P2 (parallel test execution) was closed as not qualified. This is the agreed order of work for roughly the next week. Estimates are honest ranges, not promises; each milestone has an exit test so "done" is not a matter of opinion. The owner decides the order; changing it is a one-line edit here.

## Where we start
- Full backend test run: about 42 minutes serial (it was 125 before Phase 1a). Parallel execution was tried, reached about 1.9x on the valid runs, but its approved gate could not be evaluated (no valid closing serial run); the runner stays serial. See `docs/testing/TESTSPEED_P2_STATUS.md`.
- Approved corrections waiting on `main` before this push: bounded retries for the fixture drop and initialisation, cleanup of half-created databases, the server-login leak fix, the auth walkthrough restore cleanup, the deterministic backup-crypto test.
- Next scheduled product work: **ALV-N005** (Production Procedure and Fee Catalog Foundation; depends on ALV-N003 and ALV-002-C01).

## Order of work (decided 2026-10-07)
| # | Milestone | Why in this order | Target window | Exit test (what must be true) |
|---|---|---|---|---|
| M0 | Push the approved corrections and records | stable base for everything after; the history shows honestly that parallelism was tried and not qualified | 2026-10-07 | `origin/main` equals local `main`; serial runner unchanged; nothing else in the diff than the listed corrections, documents and records |
| M1 | **ALV-N005** delivered and handed off | product progress comes first; it is the next story in the plan | 2026-10-08 to about 2026-10-11 (re-estimated after the story prompt is read; review rounds are the main uncertainty) | every acceptance criterion passes, the complete evidence sweep is clean, `.colaberry/progress.json` and the handoff package are updated, pushed after the owner's say-so |
| M2 | **Template-database plan** (Phase 1b) written and submitted for review | uses the waiting time of M1's review rounds; no code before approval | plan v1 by about 2026-10-10 | plan with measurements, design, stale-template protection, failure paths, mutation controls and a measurement protocol; reviewer decision received |
| M3 | Template database implemented and verified (only if M2 is approved) | gets most of the speed gain with no concurrency risk: projected 12 to 14 minutes off the serial run (a projection from the Phase 0 numbers, to be measured) | about 2026-10-13 to 2026-10-16 | same test ids and outcomes as the serial baseline, serial full run measured on a quiet machine before and after, a template that is out of date can never be used silently, no leaked databases, reviewer approval |
| M4 | Parallelism revisit (plan only) | only after M3, because with a faster serial run the remaining gain may be smaller or larger and the margin over the 1.8x gate changes | plan about 2026-10-17 or later (stretch for this week) | decision memo: is parallel still worth it at the new serial time? If yes: plan covering the backup classes (they are most of the serial tail), the worker count, the sampler that names the foreign load, a quiet overnight window, and a protocol on one fixed commit |

## Rules that stay in force
- No push without the owner's explicit say-so. Merge, never rebase. Commit explicit paths only.
- Every code change has a PROGRESS.md entry with real verification numbers; a reviewer's decision is stored unchanged under `.alveara/reviews/<ID>/`.
- The complete evidence sweep is mandatory for every ALV handoff.
- Measurement sessions need a quiet machine (no other agent building) for their whole duration; they are scheduled, not squeezed in.
- Leftover test databases and server logins are never deleted automatically; their cleanup waits for an ownership-based design.
- Nothing from the closed parallel session is reused as evidence for a new one.

## Decision points
- After M1 starts: the owner confirms the estimate for ALV-N005 once the prompt has been read.
- After M2: the reviewer's decision decides whether M3 goes ahead.
- After M3: measured serial time decides whether M4 is worth the effort (rule of thumb: the gain per full run times the number of full runs still ahead must clearly exceed the cost of the measurement session and review rounds).

## Housekeeping (low priority, any gap)
- Remove the scratch worktrees under `C:\tmp\par` once nobody needs the evidence checkouts.
- Rename the theory `Whatever_FIXED_bytes_follow_the_retired_container_magic...` (editorial, noted by the reviewer).
- Design the ownership-based cleanup for leftover test databases and logins.

## Status log

- **2026-10-08 - M1 (ALV-N005):** backend, frontend, documentation (`docs/PROCEDURE_CATALOG.md`) and tests are written (84 backend tests, 20 frontend tests, 6 real-backend browser tests, all passing when run on their own). The complete evidence sweep (serial backend suite about 42 minutes, frontend gates, mocked e2e, repo checks, the walkthroughs on fresh database names) is next, then the implementation commit, the handoff commit and the ZIP. The story will stop at `AWAITING_REVIEW`. On track for the 2026-10-11 estimate; review rounds remain the main uncertainty.
- **2026-10-08 (later) - M1 (ALV-N005):** attempt R01 was reviewed and returned `CHANGES_REQUIRED` (one finding: CDT provenance not enforced for every non-local code). Attempt R02 corrects it in the service and the database, with 19 new backend tests, a full serial backend run (2405 of 2405), the frontend gates, all 18 walkthroughs and two negative controls, and is packaged at `AWAITING_REVIEW`. M1 now waits for the second review; the R01 review round cost about half a day, so the 2026-10-11 estimate still holds if R02 is approved.

