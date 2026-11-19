# ALV-N024 addendum 02: negative controls (e) and (f), and the READCOMMITTEDLOCK rationale

**Raised:** 2026-11-19, during slice 6 (negative controls), under the BRIEFING-R02 stop rule ("Any newly discovered ... concurrency behavior ... must stop for independent review"). The owner decided the sequence on 2026-11-19: strengthen the control (e) tests and re-run them, do not claim that control (f) proves the hint is required, write this addendum, and stop for review before the final commit, sweep and package.
**Status:** for independent review. **No production code changes.** One test file was changed (`BillingReceiptConcurrencyTests.cs`, below). The `READCOMMITTEDLOCK` hint stays in the trigger exactly as approved.

## 1. What the approved brief said
Briefing revision 2, section 0 (answer to CR-1), says the hint is required because, under `READ_COMMITTED_SNAPSHOT ON`, "a trigger's query reads row versions as they were when its INSERT statement started, which is before it waited for the lock". Section 9 step 6 made that a negative control: "(f) The READCOMMITTEDLOCK hint is removed from the trigger together with the service lock: the concurrent direct-path test must fail, showing why the hint is needed."

## 2. What control (f) showed
Control (f) ran on 2026-11-19. Both hints were removed from the re-created `TR_FinancialDocuments_Links`. The template database is rebuilt from the edited migration for each test process. The control then ran `BillingDocumentReplacementMigrationTests.Two_concurrent_direct_inserts_of_a_first_receipt_leave_exactly_one_every_time`: two direct-SQL sessions per round, released together, 20 rounds. It **passed** (1/1, exit 0, 46 s, `control_f.log`). The file was restored and its SHA-256 matches.

**Reading:** the observation is consistent with SQL Server giving each statement inside a trigger its own read-committed snapshot. If so, the trigger's `SELECT`, which runs after its `sp_getapplock` statement has returned, already sees a competitor that committed while it waited. That would make the hint unnecessary for correctness in this flow. We have not proven this, and a single 20-round pass is not proof either way.

## 3. Change to the approved rationale (for approval)
1. **The hint stays.** It is in the approved, reviewed trigger and it does no harm to correctness. It is plausibly the cause of the direct-path wait that addendum 01 described (the lock holder's locking read waits on the other session's uncommitted row). That path is only possible for writes that bypass the service, and addendum 01 already accepts its outcome (51297).
2. **Its necessity is no longer claimed.** Section 0's sentence "Why READCOMMITTEDLOCK is required" is withdrawn. It is replaced by: "The trigger reads with READCOMMITTEDLOCK so that its check reads the latest committed rows after the lock wait, whatever the session's isolation setting; the guarantee rests on the per-payment lock and 51295, not on the hint."
3. **Control (f) is withdrawn as a requirement.** It stays in the evidence as an observation ("removing the hint did not break the 20-round direct race"), and nothing depends on it. The R01 evidence will say this in these words and will not claim that (f) proves the hint necessary.

## 4. Control (e): strengthened and re-run
**First run** (service lock removed from `IssueDocumentAsync`): all 4 tests in `BillingReceiptConcurrencyTests` failed. The losers were refused by the database (deadlock victim 1205, a stale-account conflict, and `save_failed` from the trigger's lock timeout 51297) instead of by the service. Those exceptions are not `BillingException`, so they ended each test **before** its count assertions ran. The run proved the change is detected, but not that exactly one receipt survived.

**Test change** (test file only; no production change): `TogetherAsync` now also records a database refusal as `database:<exception type>` instead of letting it end the round. Each test now checks its invariants **before** the refusal code:
- the receipt count;
- the audit count;
- that the payment's lock is free afterwards;
- in the busy test, that nothing was written.

This only reorders and widens the checks. Every assertion that existed is still there, at least as strict.

**Re-run:**
| Run | Result |
|---|---|
| Real code (`BillingReceiptConcurrencyTests`) | **4 / 4 pass** (37 s) |
| Control (e) (service lock removed) | **4 of 4 fail, each at the refusal-code assertion, after every invariant passed in every round reached**: exactly one receipt (two in the replacement test), the audit count unchanged by the loser, the lock free; in the busy test nothing written. Refusals seen: `database:InvalidOperationException` (deadlock victim), `database:ConcurrencyConflictException`, `save_failed` (51297). 83 s; restored, SHA-256 equal (`control_e.log`) |

**Reading:** with the service lock removed, the database layer alone (the trigger's lock plus 51295) still kept exactly one receipt, and the tests detect the removal. This is the layering proof that section 9 step 6 (e) asked for.

## 5. The other controls (for completeness; all as designed)
| Control | Result |
|---|---|
| (a) service one-receipt check removed | 2 of 3 `BillingDocumentReplacementTests` fail. They fail at the **preview** step: the service rule is gone, so a second preview is no longer refused. This proves the tests catch the removal. That 51295 holds on its own is proven directly by `BillingDocumentReplacementMigrationTests` (direct SQL), not by this control. |
| (b) service payer check removed | 1 of 3 `BillingPayerEligibilityTests` fails: the stranger's payment is still refused, now by the database (`save_failed` 503 instead of `payer_not_eligible` 400). The only rule that refuses it is the widened 51142, so this is the layering proof. |
| (c) adjustment term removed | `BillingReconciliationTests` property test fails (1/1). |
| (d) version 2 built with version-3 facts | 3 of 24 `FinancialDocumentTemplateVersionTests` fail, including the frozen-v2 byte-for-byte test. |

Each control ran alone, with a byte backup and a SHA-256 check after restore; all restores matched. Logs are in `C:\.alvera-data\Test-Data\Temp\n024-negctl-2026-11-19\`.

## 6. Decision requested
1. Approve the rationale change in section 3: the hint stays, its necessity is not claimed, and control (f) is withdrawn as a requirement.
2. Accept the strengthened control (e) (section 4) as the layering proof.
3. Accept the (a) and (b) readings in section 5.
After approval: the final implementation commit, the Stage 1 sweep and the R01 package.
