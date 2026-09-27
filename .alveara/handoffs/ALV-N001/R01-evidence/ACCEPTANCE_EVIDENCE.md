# ALV-N001 R01 — Acceptance Evidence

Maps each of the 6 acceptance items from the Execution Plan to the evidence proving it.

| # | Acceptance item | Evidence |
|---|---|---|
| 1 | The root STORY-000 Command Center still satisfies its course contract | `tests/story-000-coexistence.test.mjs` (4 checks, all passing) confirms `index.html`/`assets/app.js`/`assets/styles.css` at repo root are present and still fetch `.colaberry/plan.json`/`progress.json`/`manifest.json` at runtime. `git diff --stat` for `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/` across this attempt shows zero diff. Course portal verification of STORY-000 (5/5, `verified_at: 2026-09-27T19:54:59.856Z`) is unaffected since no course-managed file was touched. |
| 2 | The production Alveara app is structurally separate from that root course surface | `src/alveara-client/` (React/TS/Vite) and `src/Alveara.Api/` (ASP.NET Core) are new, independent project roots. `tests/story-000-coexistence.test.mjs` explicitly asserts both paths exist and that the client's `index.html` is a genuinely different document from the root Command Center's `index.html`. |
| 3 | Shell is usable at agreed desktop and tablet widths | `AppShell.css` defines a `@media (max-width: 820px)` breakpoint collapsing the sidebar to a top bar; `App.responsive.test.tsx` asserts the breakpoint's presence and that structural landmarks (`nav`, `main`) render regardless of viewport (layout itself is CSS-driven, not JS-conditional, so DOM structure is the correct thing to assert in jsdom). |
| 4 | Tokens and the initial reusable controls are demonstrated without pre-building unrelated feature components | `src/styles/tokens.css` defines the full token set (colour/type/spacing/radius/elevation/icon/density/focus). `ShowcasePage` (`/showcase`) demonstrates every control that exists: `Button` (3 variants + disabled), `FormField` (with live validation), `LoadingState`/`EmptyState`/`ErrorState`, and the `Notification` toast system. No component beyond what the shell/showcase need was built. |
| 5 | At least one keyboard-only path and accessibility smoke check pass | `App.keyboard.test.tsx` — keyboard-only: Tab focuses skip-link, continued tabbing reaches "Component Showcase" nav link, Enter activates navigation, confirmed via the resulting page heading. `App.accessibility.test.tsx` — `jest-axe` run against the rendered dashboard reports zero violations. |
| 6 | No acceptance criterion depends on authentication/RBAC that has not been built yet | `moduleRegistry.ts` carries an explicit doc-comment stating it performs no permission filtering because no signed-in-user concept exists yet. No component, page, or test in this attempt references a role, permission, or signed-in identity. `DashboardPage` and `ShowcasePage` render unconditionally for anyone who loads the shell. |

## Required tests (from the story prompt) — mapped

| Required test | Evidence |
|---|---|
| STORY-000 regression/smoke check | `tests/story-000-coexistence.test.mjs`, tests 1–2 |
| Shell/component tests | `components/FormField.test.tsx`; `ShowcasePage` exercises every other component |
| Keyboard/accessibility smoke test | `App.keyboard.test.tsx`, `App.accessibility.test.tsx` |
| Responsive layout smoke test | `App.responsive.test.tsx` |
| Course-root coexistence test/check | `tests/story-000-coexistence.test.mjs`, tests 3–4 |
