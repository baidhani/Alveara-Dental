# ALV-N021 A1-R02: acceptance evidence

| A1-R01 review requirement | Done |
|---|---|
| Correct the `BillingService.Timeline.cs` class documentation: live aging uses practice-local calendar days, not whole UTC days | Line 11 now reads "in whole practice-local calendar days (ALV-N021 A1-DECISION-01: the as-of and performed instants are each converted to the practice's day at their own offset, the same policy as a new version-2 statement)" |
| Do not alter the preserved original version-1 builder copy or its documentation | untouched (`git diff 807014c HEAD -- src/Alveara.Api.Tests` is empty) |
| Only the comment plus control/evidence records | the only `src` change since `807014c` is that comment line |
| A1-R02 documentation-only handoff; implementation SHA stays `807014c`; documentation commit named separately | `A1-R02.md` |
| No test run | none run |
| Preserve the R01 package, evidence and status | R01 files and ZIP unchanged |

Other comments checked: the remaining "UTC days" wording in `FinancialDocumentBuilder.cs` (lines 22, 63, 94) describes version 1 and is correct.

Finding records, the A1-DECISION-01 contract checklist and the date-site classification are unchanged: `.alveara/handoffs/ALV-N021/A1-R01-evidence/`.
