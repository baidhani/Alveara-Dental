# Alveara Dental — Build State

**This file records only what repository inspection actually proves exists right now.** It is not a roadmap. Update it truthfully after every ALV attempt.

Last updated: ALV-N001 attempt R01 (`AWAITING_REVIEW`), implementation commit `c0368655e3bea674956252b8428e5cf8c29a346b`.

## Production implementation status

**Production implementation has begun with ALV-N001 (application shell), `AWAITING_REVIEW`.** No other `ALV-*` story has started. `tests/` at the repo root now holds only the STORY-000 coexistence/regression check (`story-000-coexistence.test.mjs`); no domain/database/authentication code exists yet.

## Application shell (ALV-N001, AWAITING_REVIEW — not yet COMPLETE)

- **Stack decision:** ASP.NET Core Web API (C#, .NET 10) + React 19 + TypeScript (Vite) client, chosen for the client-independent API boundary and JS ecosystem maturity for accessibility/offline tooling. Recorded here as the durable decision; do not re-litigate per-story.
- **`src/Alveara.Api`** — ASP.NET Core Web API. Currently exposes only `GET /api/health` (liveness check consumed by the client's connection-status hook). CORS scoped to `http://localhost:5173`/`https://localhost:5173` (the Vite dev server) only.
- **`src/alveara-client`** — React + TypeScript (Vite) application shell:
  - Design tokens (`src/styles/tokens.css`): colour (light/dark), typography, spacing, radius, elevation, icon sizing, density, focus ring — single source of truth for all components.
  - `AppShell` — responsive sidebar navigation driven by `moduleRegistry.ts` (currently 2 entries: Dashboard, Component Showcase), patient-context placeholder region, light/dark theme toggle (persisted to `localStorage`), disconnected-banner wired to `useConnectionStatus` (polls `/api/health` every 15s).
  - Reusable components built so far: `Button`, `PageHeader`, `FormField` (with `aria-invalid`/`aria-describedby`/`role=alert` wiring), `LoadingState`/`EmptyState`/`ErrorState`, `DisconnectedBanner`, `Notification` (toast) system. Demonstrated on `/showcase`.
  - **No permission-aware navigation yet, by design** — `moduleRegistry.ts` lists every module unconditionally because authentication/RBAC do not exist. `ALV-N009` adds real filtering against this same registry.
  - Responsive breakpoint at 820px collapses the sidebar to a top bar.
- **Root STORY-000 Command Center is untouched** — `index.html`, `assets/app.js`, `assets/styles.css` at the repo root are unchanged; the production app lives entirely under `src/alveara-client` and `src/Alveara.Api`.

## What actually exists in the repository

- **Course scaffold** (`src/`, `docs/`, `tests/`, `.claude/agents/`, `README.md`, `PROGRESS.md`) — created by the course project-folder scaffolding step. `src/`, `tests/`, and `.claude/agents/` are empty directories.
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
