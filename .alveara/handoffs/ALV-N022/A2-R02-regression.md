# ALV-N022 A2-R02: Stage 2 result (frozen targeted contract of review A2-R02)

## Contract (review `.alveara/reviews/ALV-N022/A2-R02.md`, verbatim)
```powershell
dotnet test src/Alveara.Api.Tests --filter "FullyQualifiedName~DiagnosisFinalizedEncounterGuardTests|FullyQualifiedName~DiagnosisFinalizedGuardMigrationTests|FullyQualifiedName~DiagnosisServiceTests|FullyQualifiedName~DiagnosisApiTests|FullyQualifiedName~RealMigrationInventoryTests"
```
At implementation SHA `66c92b90dcfd8ebab245d3d6147e5fce26746d17`; exit zero; clean tree before and after; durable output with the resolved SHA and the passing count.

## How it was run
- In a fresh git worktree checked out **detached at exactly `66c92b9`** (`C:\tmp\n022s2`), so no later documentation commit was in the tested tree. The command was run once, exactly as written, from the worktree root.
- Before: resolved HEAD `66c92b90dcfd8ebab245d3d6147e5fce26746d17`, `git status --porcelain` empty (`A2-R02-regression-evidence/before.txt`).
- After: resolved HEAD unchanged, `git status --porcelain` empty (`after.txt`; build output is git-ignored).
- The worktree was removed afterwards (my own; nothing else touched).

## Result
| | |
|---|---|
| Exit code | **0** |
| Tests | **77 passed, 0 failed, 0 skipped** (test duration 1 m 20 s) |
| Wall time | 117 s (including the build of the fresh worktree), under five minutes |
| Started / finished (UTC) | `start.txt` / 2026-11-17T16:40:58Z |
| Full console output | `A2-R02-regression-evidence/stage2-console.txt` (its compiler warnings are pre-existing CS0108 warnings in test files A2 did not touch) |

Classes in the selection: DiagnosisFinalizedEncounterGuardTests (3), DiagnosisFinalizedGuardMigrationTests (2), DiagnosisServiceTests (39), DiagnosisApiTests (28), RealMigrationInventoryTests (5) = 77.

## Nothing else
No production, test, migration, configuration or evidence change before or during Stage 2 (the only commit between the Stage 1 package and this run is `195afd0`, recording the Stage 1 approval: review file, EXECUTION_STATUS, BUILD_STATE, PROGRESS). The browser workflow, broad/full suites, the chain and other walkthroughs were not run, as the contract says. Broad regression remains `DEFERRED_TO_ALV-N028`.
