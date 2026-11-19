# ALV-N024 addendum 01: the direct-SQL receipt race also ends with 51297

**Raised:** 2026-11-19, during slice 1 (database), under the BRIEFING-R02 stop rule: "Any newly discovered ... concurrency behavior ... must stop for independent review before code proceeds."
**Status:** for independent review. The approved mechanism is **unchanged**. Only the expected refusal set of one proof is widened, and the owner decided this on 2026-11-19: "Accept 51297 + disclose".

## What was observed
`BillingDocumentReplacementMigrationTests.Two_concurrent_direct_inserts_of_a_first_receipt_leave_exactly_one_every_time` runs 20 rounds. In each round, two sessions are released together by a barrier, and each inserts a first receipt for the same payment directly in SQL, bypassing the service. In one run, round 2's loser failed after about 5 s with **51297** ("Receipts are being issued for this payment right now. Try again."). Briefing revision 2, section 4.2, had expected 51295 or 1205. In every round of every run, **exactly one receipt survived**. On the next run all 20 rounds ended without a timeout. Which refusal the loser gets depends on timing.

## Why
On the direct path each session writes its row **before** its AFTER INSERT trigger runs:
1. Session A has inserted its row and its trigger holds `alveara.receipt:{payment}`. Its `READCOMMITTEDLOCK` read of that payment's receipts waits on session B's uncommitted row.
2. Session B has inserted its row, and its trigger waits for the application lock held by A.

The two sessions wait on each other. Either SQL Server picks a deadlock victim (1205), or B's 5-second `sp_getapplock` timeout expires first. The timeout raises 51297 and rolls B back, A's read proceeds, and A commits. When the timing does not overlap, B simply waits and then sees A's committed receipt (51295). All three endings leave one receipt, with no partial row, audit row or lock.

**The service path cannot form this wait.** `IssueDocumentAsync` takes the same application lock **before** it inserts (briefing section 2.1, S2). A second service caller therefore waits on the lock while holding no row, so the first caller's read is never blocked by it. The service-path concurrency proofs (section 6, CR-1 (1) and (2), slice 2) still expect `receipt_exists`, `receipt_replaced` or 503 `receipt_busy`, and nothing else.

## Change
- **Proof (3) of section 6:** "the loser fails with 51295 or 1205" becomes "the loser fails with **51295, 1205 or 51297**". The surviving count (exactly one receipt) and the single success per round are unchanged. The test records every SQL error of the loser in its failure message.
- **Section 4.2, receipt serialisation:** add "or the lock timeout (51297)" to the direct-SQL description.
- **Not changed:** the trigger, the lock, the timeout, `READCOMMITTEDLOCK`, the service design.

## Alternative considered and not taken
Reading with `READCOMMITTEDLOCK, READPAST` would let A skip B's uncommitted row. That removes the wait, and the loser would always get 51295 quickly. It was not taken because it changes the approved read semantics (skipping locked rows) to tidy up a path the application never uses, and it would need its own proof that no committed receipt can be skipped.

## Evidence
- Slice 1 focused group (7 classes, before the setup fix): 121 of 121 in the six neighbouring classes; the 7 new tests failed on a test-setup error. The account had no balance, so a payment broke the existing "no negative balance" rule; the fix funds the account first. 3 m 29 s.
- After the setup fix: `BillingDocumentReplacementMigrationTests` 6 of 7, with the race test failing on 51297 as described; then 7 of 7 after this change.
