# ALV-N019 — R04 Fixture Preflight Scope Addendum

This addendum supplements, and does not alter, the immutable R03 rerun decision.

## Required preflight before another full regression

After the R04 fixture hardening is implemented, do **not** begin the next full Stage 2 run immediately.  First run the new deterministic fixture preflight repeatedly to establish that the known pool-pruning race and failed-initialization cleanup are fixed.

The preflight must:

1. Force the known SQL client pool-pruning timing condition at the first pooled open after clone initialization; it must not rely on a lucky timing window.
2. Exercise the new bounded recovery at least **50 consecutive iterations**.  Every iteration must pass.
3. Verify after every iteration that no fixture-owned `AlveraTest_*` database or associated database files remain.
4. Exercise a deliberately induced initialization failure after the fixture has created its owned database, then prove the database and its two files are cleaned up.
5. Prove the recovery classifies and retries only the known pool-acquisition timeout.  It must not mask product errors, arbitrary SQL errors, or assertion failures.
6. Preserve raw per-iteration outcomes, cleanup checks, and the forced-race mechanism in the R04 Stage 1 package.

The preflight is a Stage 1 confidence gate, not a replacement for the final regression.  Only after independent approval of R04 Stage 1 may the agent run one new frozen Stage 2 contract, which remains necessary to validate the entire 5,697-test system and the remediated package graph together.
