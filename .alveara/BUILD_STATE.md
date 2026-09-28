# Alveara Dental — Build State

**This file records only what repository inspection actually proves exists right now.** It is not a roadmap. Update it truthfully after every ALV attempt.

Last updated: ALV-N001 attempt R02 (`AWAITING_REVIEW`) — see `.alveara/EXECUTION_STATUS.json` for the exact implementation commit SHA.

## Production implementation status

**Production implementation has begun with ALV-N001 (application shell), `AWAITING_REVIEW`.** No other `ALV-*` story has started. `tests/` at the repo root now holds only the STORY-000 coexistence/regression check (`story-000-coexistence.test.mjs`); no domain/database/authentication code exists yet.

## Application shell (ALV-N001, AWAITING_REVIEW — not yet COMPLETE)

- **Stack decision:** ASP.NET Core Web API (C#, .NET 10) + React 19 + TypeScript (Vite) client, chosen for the client-independent API boundary and JS ecosystem maturity for accessibility/offline tooling. Recorded here as the durable decision; do not re-litigate per-story.
- **`src/Alveara.Api`** — ASP.NET Core Web API. Currently exposes only `GET /api/health` (liveness check consumed by the client's connection-status hook). CORS scoped to `http://localhost:5173`/`https://localhost:5173` (the Vite dev server) only. **Canonical dev port: 5072** (the `http` profile in `Properties/launchSettings.json`) — the client's Vite dev proxy (`vite.config.ts`) targets this exact port; keep them in sync if either changes.
- **`src/alveara-client`** — React + TypeScript (Vite) application shell:
  - Design tokens (`src/styles/tokens.css`): colour (light/dark), typography, spacing, radius, elevation, icon sizing, density, focus ring — single source of truth for all components. Dark-theme `--color-primary`/`--color-primary-hover` are `#1f4459`/`#27587a` (≥7.5:1 white-text contrast; a regression test in `src/styles/contrast.test.ts` enforces ≥4.5:1 for both themes after an earlier attempt shipped ~2.8:1/~2.2:1).
  - `AppShell` — responsive sidebar navigation driven by `moduleRegistry.ts` (currently 2 entries: Dashboard, Component Showcase; tolerates an empty registry without crashing), patient-context placeholder region, light/dark theme toggle (persisted to `localStorage`), disconnected-banner wired to `useConnectionStatus`.
  - `useConnectionStatus` polls `/api/health` every 15s, validates both HTTP status **and** the JSON body shape (`{status:"ok"}`) — a bare `res.ok` check previously treated Vite's dev-server HTML fallback (a real 200) as a healthy API when no proxy/listener was behind it. Each check now also times out after 4s and never overlaps a still-in-flight check.
  - Reusable components: `Button`/`ButtonLink` (the latter for link-styled navigation actions — never nest a `<button>` inside an `<a>`), `PageHeader`, `FormField` (labelled, `aria-invalid`/`aria-describedby`/`role=alert`; `aria-describedby` only ever references an id that is actually rendered), `LoadingState`/`EmptyState`/`ErrorState`, `DisconnectedBanner`, `Notification` (toast) system. Demonstrated on `/showcase`.
  - **No permission-aware navigation yet, by design** — `moduleRegistry.ts` lists every module unconditionally because authentication/RBAC do not exist. `ALV-N009` adds real filtering against this same registry.
  - Responsive breakpoint at 820px collapses the sidebar to a top bar; verified with real-browser (Playwright/Chromium) tests at both a 1280×800 desktop and a 768×1024 tablet viewport, not just DOM-structural jsdom checks.
  - Real-browser accessibility smoke coverage (`e2e/accessibility.spec.ts`, axe via Playwright) now spans Dashboard, Showcase, and the not-found page, in both light and dark theme — not the dashboard alone.
- **Root STORY-000 Command Center is untouched** — `index.html`, `assets/app.js`, `assets/styles.css` at the repo root are unchanged; the production app lives entirely under `src/alveara-client` and `src/Alveara.Api`.
- **Test tooling:** `npm run test` (vitest, jsdom) for unit/component tests; `npm run test:e2e` (Playwright, real Chromium) for browser-rendered shell/accessibility tests — the two are mutually exclusive by file location (`e2e/` vs. everywhere else) and vitest's config explicitly excludes `e2e/`.

## What actually exists in the repository

- **Course scaffold** (`docs/`, `README.md`, `PROGRESS.md`, `.claude/agents/`) — created by the course project-folder scaffolding step. `.claude/agents/` remains an empty directory. `src/` and `tests/` were also part of the scaffold but are **no longer empty** — see "Application shell" below; `src/` now holds the ALV-N001 implementation and `tests/` holds the STORY-000 coexistence check.
- **Git repository** — initialized, connected to GitHub origin `https://github.com/baidhani/Alveara-Dental`, branch `main`. A push webhook to `https://enterprise.colaberry.ai/api/webhook/github` was registered and observed delivering successfully (HTTP 200) at the time of the STORY-000 session; this is repository-history evidence of past activity, not a live re-check of current service configuration, and should be reconfirmed before being relied upon.
- **`.colaberry/`** — course/platform-controlled. Contains `connect.txt` (pairing proof), `plan.json`, `progress.json`, `manifest.json`, `profile.json`, written/rewritten by the course platform's sync process. Not modified by this bootstrap.
- **`docs/`** — course/platform-controlled. Contains `STORIES.md`, `REQUIREMENTS.md`, `TRACEABILITY.md`, `DATA_CONTRACT.md`, and `docs/stories/STORY-000.md` through `STORY-018.md`. Not modified by this bootstrap.
- **`CLAUDE.md`** — present at repository root, course-provided. Not modified by this bootstrap.
- **STORY-000 Command Center** (`index.html` at repo root, `assets/app.js`, `assets/styles.css`, `assets/sample/*.json`) — course story, **genuinely verified by the course portal**: 5 of 5 Done-means criteria passed, `verification.state = "verified"`, `verified_at = 2026-09-27T19:54:59.856Z`, implementation commit `5d67b100eb4d363e714d362cc07414ea7fc4ba7a`. It reads `.colaberry/plan.json`, `progress.json`, and `manifest.json` at runtime, supports a Sample/Real data toggle and a light/dark theme, and covers all nine required tabs (Overview, Outcomes, Users & Use Case, Guardrails, Systems, Project Management, AI Agents, Knowledge Base, Data Model).
- **GitHub Pages** — as of the STORY-000 session, optional Step 4 had not been exercised, so Pages was not enabled at that time. Not independently reconfirmed by this bootstrap; no live GitHub API check was performed.

## What does not exist yet

- Any architecture/deployment/database/background-job foundation (`ALV-N002`).
- Authentication, RBAC, MFA, audit logging, or any security control beyond what the course portal's own STORY-001/002 will eventually require.
- Any patient, scheduling, clinical, billing, document, or reporting functionality.
- Any encrypted backup/restore capability.
- Any of the remaining 33 first-release `ALV-*` stories' implementation (only `ALV-N001` has started, and it is `AWAITING_REVIEW`, not `COMPLETE`).

## Known limitations / open items

- GitHub Pages was not enabled as of the STORY-000 session (previously reported, not independently reconfirmed here); until confirmed otherwise, treat the Command Center as reachable only by opening `index.html` locally or via a local static server.
- `.colaberry/plan.json`'s `agents[]` array is empty; the Command Center's "AI Agents" tab renders story ownership (from the Execution Plan) rather than a real scoped agent roster, and says so on the tab.
