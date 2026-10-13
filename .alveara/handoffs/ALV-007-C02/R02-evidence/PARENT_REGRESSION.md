# ALV-007-C02 R02 - Parent regression

The parent course story is `STORY-007` (its completion contract is immutable and must keep passing). The dependencies' contracts, `ALV-007-C01` and `ALV-008-C01`, were run as well.

| Suite | Result |
|---|---|
| STORY-007 backend: `BillingSchemaTests`, `BillingServiceTests`, `BillingApiTests` and the rest of the `Billing*` classes written for it | pass inside the full serial run (3206 of 3206). **One disclosed edit** (below) |
| STORY-007 frontend: `BillingPanel.test.tsx` | pass, unchanged file |
| STORY-007 real-browser walkthrough `billing-real-backend.spec.ts` | 10 of 10, unchanged file (the first sweep exposed a class clash in the new panel; the panel was fixed, the parent spec was not touched) |
| ALV-007-C01 backend: `BillingLedgerMathTests`, `BillingLedgerSchemaTests`, `BillingLedgerServiceTests`, `BillingLedgerApiTests`, `BillingResponsibilityServiceTests` and `BillingProcedureChargeContractTests` | pass, unchanged files, inside the full run |
| ALV-007-C01 frontend: `BillingLedgerPanel.test.tsx` | pass, unchanged file |
| ALV-007-C01 real-browser walkthrough `billing-ledger-real-backend.spec.ts` | 11 of 11, unchanged file |
| ALV-008-C01 backend: `CompletedProcedure*`, `CompletionRulesTests`, `CompletionApiTests` | pass, unchanged files; `CompletedProcedureMigrationTests` rolls back through this story's migration |
| ALV-008-C01 real-browser walkthrough `completion-real-backend.spec.ts` | 11 of 11, unchanged file |
| The other 21 real-backend walkthroughs of earlier stories | all pass first time (sweep: 25 runs, 267 of 267) |
| Vitest, mocked Playwright, repository checks | 1387 of 1387, 136 of 136, 8 of 8 |

## The one parent test edited
`BillingSchemaTests.The_database_refuses_an_entry_that_breaks_a_shape_rule` (line 102) used the literal `"Refund"` as an example of a kind the database must refuse. `Refund` is now a valid kind (it is the point of this story), so the example is `"Bogus"`. The assertion, the rule and the test name are unchanged.

## Parent behaviour this story changes on purpose
Three database triggers of earlier stories are replaced, each with the old behaviour kept when there are no refunds or releases (the rollback restores the old text verbatim):
- `TR_BillingAllocations_Links` (51130, 51131): the payment's cap is reduced by what was refunded and both caps count net of releases.
- `TR_BillingEntries_Reversals` (51141): an allocation is counted net of its releases, so a charge whose money was all refunded or moved away can be reversed. This also fixes a defect found by the reconciliation (see `TEST_RESULTS.md`).
- The account view gains optional fields at the end of its records (`refundsEntryNumber`, `replacesEntryNumber`, `refundedAmount`, `refundableAmount`, `releasedAmount`) and a `Refund` entry kind; the existing field names, order and meaning are unchanged, and the frontend client adds them as optional.
