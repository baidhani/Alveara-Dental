# ALV-N020-C02 R01: test results

2026-11-12. All runs under the owner's standing five-minute permission; expected durations from C01's timings. Raw output in `artifacts/focused-proof/`. Working folder `src/alveara-client`.

| # | Command | Expected | Actual | Result | Raw |
|---|---|---|---|---|---|
| 1 | `npm run build`, then `npx playwright test e2e/login-layout-polish.spec.ts --project=desktop -g capture` with POLISH_SHOTS_LABEL=before (current build, before any change) | < 1 min | 19 s | pass, 8 PNGs | `screenshots/before/` |
| 2 | after the change: `npx tsc -b`; `npm run build`; `npx playwright test e2e/login-layout-polish.spec.ts --project=desktop`; `npx vitest run src/pages/login` | < 1 min | 40 s | browser 7 passed 1 skipped; unit 4 files 40 tests | (console) |
| 3 | `... -g capture` with label `after` | < 30 s | ~5 s | pass, 8 PNGs | `screenshots/after/` |
| 4 | negative controls NC1-NC4 (one rule reverted at a time, rebuild, run the new spec, then restore and run again) | ~2 min | 108 s | NC1 `Username 360 vs Sign in 420`; NC2 `space above the logo` (desktop); NC3 `notice font size`; NC4 `rendered ratio 280x126`; restored 7/7 | summarised below |
| 5 | `npx playwright test e2e/login-presentation.spec.ts e2e/login-presentation-access.spec.ts e2e/shell.spec.ts e2e/login-banner-contrast.spec.ts e2e/login-layout-polish.spec.ts --project=desktop` | < 1 min | 55 s | 37 passed, 1 skipped (capture) | `browser-all.txt` |
| 6 | `npx vitest run` (whole client unit suite) | ~3 min | 161 s | 128 files, 1953 tests passed | `vitest.txt` |
| 7 | `npx tsc -b`; `npm run lint` | - | - | tsc clean (empty output); lint 42 warnings (baseline unchanged) | `tsc.txt`, `lint.txt` |
| 8 | SVG negative control: one byte appended, `npx vitest run src/pages/login/loginLogo.test.ts`, file restored | < 10 s | 3 s | 1 failed (`is byte-identical to the reviewed owner-supplied file`), 2 passed; hash re-checked identical | `nc-svg.txt` |

Negative-control mapping: the width rule is NC1 (the scoped `max-width: none` reverted to 360 px); the spacing rule NC2 (old margins); the 12 px notice NC3 (back to the 13 px token); the SVG ratio NC4 (the old 280/126 slot ratio) and the SVG integrity test row 8.

Not run: the full backend suite, the chain, walkthroughs and any other broad regression (deferred to ALV-N028).
