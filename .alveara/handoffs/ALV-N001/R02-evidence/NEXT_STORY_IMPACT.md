# ALV-N001 R02 — Next Story Impact

Carries forward R01's `NEXT_STORY_IMPACT.md` (repository path: `.alveara/handoffs/ALV-N001/R01-evidence/NEXT_STORY_IMPACT.md`; that attempt's packaged ZIP path was `review/NEXT_STORY_IMPACT.md` — the two are the repo-evidence copy and the packaged copy of the same content, not different documents). All of R01's assumptions/interfaces/migrations/extension points still hold. This file adds only what changed in R02.

## New facts the next stories can rely on

- **Canonical dev-time API port is 5072**, fixed end-to-end: `src/Alveara.Api/Properties/launchSettings.json`'s `http` profile and `src/alveara-client/vite.config.ts`'s dev proxy target must be kept in sync. `ALV-N002` should preserve this if it changes launch profiles.
- **`useConnectionStatus` now validates payload shape, not just HTTP status.** Any later story that changes `/api/health`'s response shape must keep the `{ status: "ok" }` field, or update the hook's `isHealthy` check in the same change.
- **`ButtonLink` exists** (`src/components/Button.tsx`) for any navigation action that should look like a button — use it instead of nesting a `<button>` inside a `<Link>`.
- **`e2e/` is the real-browser Playwright suite location**, excluded from vitest. Later UI stories should add their own `e2e/*.spec.ts` files here for genuine rendered/interaction evidence, rather than relying solely on jsdom tests for acceptance items like "usable at width X" or "keyboard-only path works."
- **`contrast.test.ts`** is a reusable pattern — later stories introducing new token-based colour combinations should add a similar computed-contrast assertion rather than eyeballing hex values.

## Unresolved from R01, still unresolved

- Database engine choice remains `ALV-N002`'s decision.
- Production same-origin deployment strategy remains `ALV-N013`'s decision.
