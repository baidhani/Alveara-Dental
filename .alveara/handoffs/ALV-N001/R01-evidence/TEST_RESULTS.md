# ALV-N001 R01 — Test Results

## Client (`src/alveara-client`) — `npm run test` (vitest)

```
 Test Files  6 passed (6)
      Tests  8 passed (8)
```

| File | Tests | Purpose |
|---|---|---|
| `App.accessibility.test.tsx` | 1 | axe accessibility smoke test on the rendered shell (required test) |
| `App.keyboard.test.tsx` | 1 | keyboard-only path: skip-link → tab to nav → Enter to navigate (required test) |
| `App.responsive.test.tsx` | 2 | structural landmark smoke test + tablet-breakpoint presence check (required test) |
| `App.routing.test.tsx` | 1 | unknown-route failure path renders `NotFoundPage`, not a blank screen |
| `App.disconnected.test.tsx` | 1 | disconnected/local-server-unavailable failure path shows a visible banner |
| `components/FormField.test.tsx` | 2 | component validation/focus-error failure path (ARIA wiring) |

## Client — `npm run build`

```
✓ 44 modules transformed.
dist/index.html                   0.46 kB │ gzip:  0.30 kB
dist/assets/index-*.css          10.10 kB │ gzip:  2.58 kB
dist/assets/index-*.js          266.72 kB │ gzip: 84.57 kB
✓ built in 962ms
```

Production build succeeds with zero TypeScript errors (after fixing `verbatimModuleSyntax` type-only import violations found during this attempt).

## API (`src/Alveara.Api.Tests`) — `dotnet test`

```
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 430 ms
```

`HealthEndpointTests.Health_endpoint_returns_ok_so_the_client_shell_can_detect_connectivity` — integration test via `WebApplicationFactory<Program>`, confirms `GET /api/health` returns 200.

## API — `dotnet build` (solution-wide)

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Manual runtime verification

- Started `dotnet run` for `Alveara.Api` on `http://localhost:5199`; `curl http://localhost:5199/api/health` returned `200 {"status":"ok"}`.
- Started `npm run dev` for `alveara-client` on `http://localhost:5173`; `curl http://localhost:5173/` returned `200` with the expected `<title>Alveara Dental</title>` shell HTML.
- Both dev servers were stopped after verification; no background processes were left running.

## Repo-root STORY-000 regression / course-root coexistence — `node --test tests/story-000-coexistence.test.mjs`

```
✔ root STORY-000 Command Center entry point still exists
✔ root Command Center still reads .colaberry/* at runtime rather than hard-coding plan content
✔ the production Alveara app lives in its own separate project paths
✔ the production app did not replace or rename the root Command Center
ℹ tests 4
ℹ pass 4
ℹ fail 0
```

## Overall

23 automated checks across 3 tooling stacks (vitest, dotnet test, node --test), all passing. 2 production builds (client + API solution) succeed with zero errors.
