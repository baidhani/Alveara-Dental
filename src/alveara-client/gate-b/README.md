# Gate B evidence tooling

Reproducible checks used by the Gate B evaluation (`.alveara/gates/B/GATE_B_REPORT.md`). They are evidence tooling, not product tests, and are not part of `npm test`. The Gate B evidence otherwise comes from the existing suites (see the report): `dotnet test`, `npx vitest run`, `npx playwright test`, and the real-backend walkthrough configs (`playwright.*.config.ts`, `gate-a/playwright.gate-a.config.ts`).

## Tablet-width variants (criterion B6, "responsive breakpoint check")

`make-tablet-specs.mjs` derives tablet variants of six existing real-backend walkthroughs **without editing them**: registration, patient workspace, forms, scheduling, calendar and STORY-011 flow. Each generated spec (`gate-b/tablet-generated/<name>.tablet.spec.ts`, gitignored build output) is the original with

- every browser context created at the project's declared tablet viewport (768 x 1024, `playwright.config.ts` `TABLET_VIEWPORT`),
- a probe that records the largest **steady** sideways overflow of the page (`scrollWidth - clientWidth`, at a real viewport width, the same value at two consecutive samples 150 ms apart; the first version of the probe counted transient values while a page was being created and reported false overflows), and
- one final test that fails if any page the walkthrough visited scrolled sideways by more than 1 px, or if the probe never ran.

The walkthroughs' own assertions and axe scans therefore run at tablet width too. One deliberate substitution: at 768 px the open appointment drawer covers the shell's theme toggle, so in the calendar and flow variants the "switch theme" step sets the same `data-theme` attribute and storage key the toggle sets instead of clicking it (the generator fails if that line is not found exactly once).

`probe-control` (no backend) proves the probe can fail: a page with a 1000 px element in a 768 px viewport must be recorded, and the login page must record 0.

### Coexistence with `npm test` (Gate B review finding B-REV-01)

The generated specs are Playwright specs and live under the client project, so `vitest.config.ts` excludes every `gate-*/` directory (and `e2e/`); otherwise `npx vitest run` collects them and fails once the tooling has run. `tests/gate-tooling-coexistence.test.mjs` (part of `node --test tests/*.test.mjs`) generates the output exactly as the gate does and asks `vitest list --filesOnly` what it would run: only product tests under `src/` may appear, with no command-line exclusions.

### Run

From `src/alveara-client`, with a real API on a fresh migrated database as for the other real-backend specs (`docs/testing/REAL_BACKEND_E2E.md`):

```
node gate-b/make-tablet-specs.mjs
TABLET_SPEC=calendar-real-backend npx playwright test --config=gate-b/playwright.tablet.config.ts
TABLET_SPEC=probe-control          npx playwright test --config=gate-b/playwright.tablet.config.ts
```

`TABLET_SPEC` is one of `patient-registration-real-backend`, `patient-workspace-real-backend`, `forms-real-backend`, `schedule-real-backend`, `calendar-real-backend`, `patient-flow-real-backend`, `probe-control`. Each walkthrough needs its own fresh database (the first-admin bootstrap is refused on a database that already has an admin).
