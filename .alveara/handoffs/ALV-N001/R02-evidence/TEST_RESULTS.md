# ALV-N001 R02 — Test Results

Corrects R01's arithmetic error (that attempt's summary said "23 checks," which the reviewer correctly identified as wrong — the real total was 13 automated tests: 8 client + 1 API + 4 coexistence, plus separate manual build/dev-server checks that are not "tests").

## Client (`src/alveara-client`) — `npm run test` (vitest, jsdom)

```
 Test Files  9 passed (9)
      Tests  20 passed (20)
```

| File | Tests | Purpose |
|---|---|---|
| `App.accessibility.test.tsx` | 1 | jsdom axe smoke test on the rendered shell |
| `App.keyboard.test.tsx` | 1 | keyboard-only path |
| `App.responsive.test.tsx` | 2 | structural landmark + breakpoint-presence check |
| `App.routing.test.tsx` | 1 | unknown-route failure path |
| `App.disconnected.test.tsx` | 1 | disconnected failure path (integration-level) |
| `components/FormField.test.tsx` | 3 | validation/error + simultaneous hint+error (new) |
| `hooks/useConnectionStatus.test.ts` (new) | 6 | up / down / HTML-fallback-as-200 / invalid-payload / timeout / recovery |
| `styles/contrast.test.ts` (new) | 4 | WCAG contrast regression, both themes, primary + hover |
| `app/AppShell.emptyRegistry.test.tsx` (new) | 1 | empty module registry failure path |

## Real browser (`src/alveara-client`) — `npm run test:e2e` (Playwright, Chromium)

```
32 passed (6.1s)
```

| File | Tests | Coverage |
|---|---|---|
| `e2e/shell.spec.ts` | 20 (10 × 2 viewports) | dashboard load/overflow, navigation + showcase render + long-text wrap, keyboard-only path, unknown route, form validation, notifications, disconnected/connected/recovery (mocked `/api/health` route), theme toggle + persistence, desktop-vs-tablet sidebar layout |
| `e2e/accessibility.spec.ts` | 12 (3 pages × 2 themes × 2 viewports) | axe (WCAG 2 A/AA) on Dashboard, Showcase, Not-Found — both themes |

Run against `npm run preview` (the actual production build), at the two viewports declared in `playwright.config.ts`: desktop `1280×800`, tablet `768×1024`.

## Client — `npm run build`

```
✓ 44 modules transformed.
dist/index.html                   0.46 kB │ gzip:  0.30 kB
dist/assets/index-*.css          10.10 kB │ gzip:  2.58 kB
dist/assets/index-*.js          267.22 kB │ gzip: 84.66 kB
✓ built in 130ms
```

Zero TypeScript errors.

## API (`src/Alveara.Api.Tests`) — `dotnet test`

```
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 430 ms
```

## API — `dotnet build` (solution-wide)

```
Build succeeded. 0 Warning(s). 0 Error(s).
```

## Manual runtime reproduction of N001-R01-01's exact scenario, and its fix

1. Started only `npm run dev` (Vite) on port 5173, with the new `/api` proxy configured, and **no API process running**.
   `curl -i http://localhost:5173/api/health` → **`502 Bad Gateway`** (the proxy correctly reports the real API is unreachable — this is the fixed behavior; previously this same request returned a fake `200` HTML fallback).
2. Started `dotnet run --urls http://localhost:5072` for `Alveara.Api`.
   `curl -i http://localhost:5173/api/health` → **`200 {"status":"ok"}`**, proxied through to the real API.
3. Both dev servers were stopped after verification; no background processes were left running.

## Repo-root STORY-000 regression / course-root coexistence — `node --test tests/story-000-coexistence.test.mjs`

```
✔ 4 tests, all passing (unchanged from R01)
```

## Overall

**13 automated tests in R01 → 57 automated tests in R02** (20 vitest + 32 Playwright + 1 API + 4 coexistence). 2 production builds succeed with zero errors. The specific defect the reviewer reproduced (HTML-fallback-as-200) was independently reproduced here too, and confirmed fixed via the real dev server + API, not just a unit test.
