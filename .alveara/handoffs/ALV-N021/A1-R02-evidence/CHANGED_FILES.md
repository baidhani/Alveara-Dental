# ALV-N021 A1-R02: changed files

Documentation/control commit `cd8342c0562eb1d1349ff752f2dd0cd1d494eb6b` (on top of the review record `e97a079`):

```
 PROGRESS.md                                                     | 6 ++++++
 src/Alveara.Api/Architecture/Billing/BillingService.Timeline.cs | 2 +-
 2 files changed, 7 insertions(+), 1 deletion(-)
```

```diff
-/// - <b>Aging</b>: what is open on each charge, aged from the procedure's performed time (as amended, latest first) in whole UTC days, in the buckets 0-30, 31-60, 61-90 and over 90.
+/// - <b>Aging</b>: what is open on each charge, aged from the procedure's performed time (as amended, latest first) in whole practice-local calendar days (ALV-N021 A1-DECISION-01: the as-of and performed instants are each converted to the practice's day at their own offset, the same policy as a new version-2 statement), in the buckets 0-30, 31-60, 61-90 and over 90.
```

Not changed: every other source and test file, the original version-1 builder copy (`OriginalFinancialDocumentBuilderV1.cs`) and its header, the A1-R01 handoff, evidence and ZIP.
