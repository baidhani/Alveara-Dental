# ALV-N021 A2-R02: frozen focused Stage 2 result

Contract: `.alveara/reviews/ALV-N021/A2-R02.md` (Stage 1 APPROVED, frozen focused Stage 2 contract), recorded in `cec6f68`.

| Item | Value |
|---|---|
| Implementation SHA | `b7efad658138698547d1b887cb8789942c1ee350` (checked out detached for the run; `git rev-parse HEAD` printed it) |
| Command (exactly as frozen, from the repository root) | `dotnet test src/Alveara.Api.Tests --filter "FullyQualifiedName~TimeCultureGuardTests\|FullyQualifiedName~ChainHostClockParseTests"` |
| Start | 2026-11-16T12:05:38-06:00 |
| End | 2026-11-16T12:07:04-06:00 |
| Elapsed | 85 s, including the build the command performs (expected under five minutes; R02 measured ~40 s with a build on a warmer cache) |
| Exit code | 0 |
| Result | **Passed: 33, Failed: 0, Skipped: 0, Total: 33** (test time 4 s) |
| Clean-tree check | 0 changed paths before checkout, 0 at the SHA, 0 after the run; then back on `main` at `cec6f68` |

Raw: `A2-R02-regression-evidence/stage2-console.txt` (the command's full console output) and `stage2-record.txt` (SHA, times, exit code, tree checks).

Nothing else was run: no backend group, walkthrough, browser, chain, mocked-browser or broad run. Broad regression stays `DEFERRED_TO_ALV-N028`; not release-certified.

Returned for the closure review.
