# ALV-018-C01 R01 failed Stage 2 evidence (copied, not moved)

- Original source path: `C:/tmp/stage2-ALV-018-C01` (outside the repository; left in place)
- Run: frozen `run_stage2.sh` on run commit `b2870675a5e84913fbcbfc6340b931e042c44d0f`, implementation `1de9de5f08b903fe6b3d1776ffd3b22cddc282d6`, started 2026-10-27T09:54:06-05:00
- Verdict: STAGE 2 DOES NOT QUALIFY (5,332 passed, 1 failed: `TestDatabaseInUseTests.A_holder_that_never_leaves_is_a_failure_after_the_bounded_attempts_with_the_first_3702_unchanged`, Expected 4, Actual 5). Ruling: `../R01-stage2.md`
- Content kept intact: RESULT, summary, compare, backend TRX and output, telemetry, leak counts, runner proof, gate logs, walkthrough outputs and screenshots, generated export packages.
- Scan before copying (2026-10-27): no secret values (the only keyword hit is the table name `PasswordResetTokens` in an excluded-tables list); no real personal data. Every record is synthetic: patient names carry a timestamp suffix, phones are 555-xxx, the only email is `ann@example.test`, the document files are 86 and 87 bytes of test content. Both generated ZIP packages were extracted and scanned as well.
- `INVENTORY.sha256` lists the SHA-256 of every other file in this folder (paths relative to the folder).
- This is a failed attempt's record. It is not a pass and must not be reused as one.
