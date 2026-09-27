# ALV-N001 R01 — Changed Files

## Implementation commit (`c0368655e3bea674956252b8428e5cf8c29a346b`)

| Group | Files | Why |
|---|---|---|
| Repo hygiene | `.gitignore` | Added `dist/`, `bin/`, `obj/`, `*.user`, `.vs/`, `node_modules/.tmp/` so build artifacts from the new projects aren't committed. |
| API project | `src/Alveara.Api/*`, `src/Alveara.Api.Tests/*`, `src/Alveara.slnx` | ASP.NET Core Web API scaffold + health endpoint + CORS policy + integration test project + solution file. |
| Client project | `src/alveara-client/*` (57 files total across both projects) | React + TypeScript (Vite) application shell: tokens, global styles, `AppShell`, module registry, connection-status hook, 8 reusable components (+ CSS), 3 pages, 6 test files, TypeScript/Vitest config. |
| Regression test | `tests/story-000-coexistence.test.mjs` | STORY-000 regression / course-root coexistence check, using the previously-empty course-scaffolded `tests/` directory. |

## Evidence commit (this commit, created after implementation)

| File | Why |
|---|---|
| `.alveara/handoffs/ALV-N001/R01.md` | Attempt-level handoff. |
| `.alveara/handoffs/ALV-N001/R01-evidence/*` | This attempt's evidence (test results, acceptance mapping, parent regression, demo evidence, changed files, Git state, next-story impact). |
| `.alveara/BUILD_STATE.md` | Updated with the new durable facts: stack decision, what `ALV-N001` actually built, current AWAITING_REVIEW status. |
| `.alveara/EXECUTION_STATUS.json` | `ALV-N001` record moved to `AWAITING_REVIEW`, attempt `R01`, acceptance 6/6, tests passed, implementation commit recorded. |

No `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, or `assets/` file was touched by either commit.
