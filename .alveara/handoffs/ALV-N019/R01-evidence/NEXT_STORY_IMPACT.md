# ALV-N019 R01: next-story impact

Next scheduled item: ALV-N020 (item 57, R05 PKG-02 + PKG-03). Release engineering (ALV-N013, ALV-N014, ALV-N008) stays locked.

## What later stories can rely on
- `scripts/Invoke-AccumulatedChain.ps1` runs stage 0 on a run-owned database. Later stages are built inside the packages that own them (R05 section 6). Add a spec named `chain/stage-<n>.spec.ts`; the config matches `stage-*.spec.ts`, and the runner would need its Playwright step extended to run later stages in order.
- A chain step title starts with its source id (for example `1.10 ...`) and may carry finding ids (`F-037`). The evidence builder turns a failing step whose finding has an OPEN entry in `TRACKED_EXCEPTIONS.json` into `TRACKED_EXCEPTION` (never a green expected failure) and keeps the run red. ALV-N028 runs `requireNoBlocking`.
- A story that closes a finding must set its tracked-exception entry to `CLOSED` with `closedEvidence`, or the builder reports the entry stale. 22 findings are seeded OPEN with owners ALV-N020..N027.
- `docs/stabilization/DISPOSITION_REGISTER.json` is hand-maintained now. Add a row for any new raw id; `Test-DispositionCompleteness.mjs` fails otherwise.
- `docs/stabilization/BRAND_INVENTORY.json` pins what keeps the Alveara spelling. A new live "Alveara" in product source fails `tests/brand-inventory.test.mjs`.

## Interfaces and conventions changed
- New downloads and backups use `dentalkeep-` file names; old `alveara-` files still work. Export `source.system` is "DentalKeep"; import accepts both. Backup archive checks match the stable code `not_a_backup`, never message text.
- The dev start scripts exit 1 with one line on a refusal (`Stop-DevScript`).
- `vitest.config.ts` excludes `chain/**`; a new Playwright-only folder needs the same exclusion (`tests/gate-tooling-coexistence.test.mjs` fails otherwise).
- PowerShell 5.1: `-match` and `-ne` are case-insensitive (use `-cmatch`, `-cne` in safety gates); `-Encoding utf8` writes a BOM, so Node readers must strip it; the repo path contains `[ ]`, so paths need `-LiteralPath` or `[WildcardPattern]::Escape`.

## Unresolved decisions that affect later items
- **PD-26 / F-001**: a vulnerable shipped dependency remains; ALV-N019 cannot close on it without the owner's choice.
- **The owner's ten-story consolidation** is uncommitted. The architecture-map catalog needs entries for ALV-N019..N028 once it is committed.
- **PD-38** (first-run administrator setup and database creation by the API, `docs/design/OWNER_REQUEST_FIRST_RUN_SETUP.md`): not authorised; home ALV-N027 (with PD-04/F-016) and ALV-N013. Nothing in this story implements it.
- **Chain clock (PD-32)**: no injected clock exists yet. The first package that needs fixed time must add the chain-only composition under a new pre-implementation review (O-1).
- **R05 6.5 vs PKG-15**: F-015 is said to be covered by PKG-15's after-login failure message, but PKG-15 has no such item; F-015 belongs to PKG-02 (ALV-N020).
- **`auth-real-backend` intermittency**: a strict-mode violation on `getByText(adminUsername)` (account menu plus users table) and a "browser closed" flake were seen; neither is fixed here. ALV-N020 touches sign-in and the account area and should decide whether to fix the test or the page.

## Prompt changes relevant to the next item
None required by this story.
