# Gate A evidence scripts

Reproducible checks used by the Gate A evaluation (`.alveara/gates/A/GATE_A_REPORT.md`). They are evidence tooling, not product tests, and are not part of `npm test`.

| Config | Spec | Needs | Produces |
|---|---|---|---|
| `playwright.gate-a.config.ts` | `gate-a-browser.spec.ts` | real API on a fresh migrated DB (`AuthAttemptRateLimit__PermitLimit=500` for role seeding) | A2 axe results on every shell route/theme, A6 per-role navigation vs access matrix |
| `playwright.a1.config.ts` | `gate-a-command-center.spec.ts` | nothing (serves the repo root statically) | A1 STORY-000 Command Center check |
| `playwright.extended.config.ts` | `../e2e/auth-real-backend.spec.ts` (unmodified) | real API | the real-backend suite with longer timeouts |
| `playwright.timing.config.ts` | `gate-a-keygen-timing.spec.ts` | real API | recovery-key generation latency |

Set `GATE_A_OUT` to choose where JSON results are written. Run from `src/alveara-client`: `npx playwright test --config=gate-a/<config>`.
