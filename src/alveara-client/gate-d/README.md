# Gate D evidence tooling

Gate tooling, not product tests. Methods and thresholds are the predeclared D1-D5 rows in `.alveara/QUALITY_GATES.md`, unchanged.

D1-D4 use the existing evidence (the financial reconciliation test, the completion and reconciliation tests, the billing statement/refund/reversal walkthroughs, and the prescription walkthroughs). D5 has no single predeclared check, so its tooling is backend-side:

| File | Criterion | What it does |
|---|---|---|
| `src/Alveara.Api.Tests/GateDHistoryIntegrityTests.cs` | D5 | Through the real services and a real SQL Server database, with synthetic data: the same payment, adjustment, refund, reversal, statement and completion submitted twice must be quiet and change nothing; the same key for a different request is refused; a stale edit (out-of-date account, plan or amendment number) is refused with a classified conflict, never a raw database error; after each, a checksum of every named table is unchanged; two simultaneous payments from one account version end with one payment and one conflict. The preserved history then reconciles (ledger verification and completion reconciliation clean) and direct UPDATE/DELETE attempts on the preserved financial and completed-procedure records are rejected by a named trigger (SQL error 51000 or above, never a type or constraint error) with the table unchanged. Evidence is written to `GATE_D_EVIDENCE_OUT` as `d5-*.json` and `d5-*.md`. |

Run: `GATE_D_EVIDENCE_OUT=<dir> dotnet test src/Alveara.Api.Tests --filter "FullyQualifiedName~GateDHistoryIntegrityTests"`.

Mutation check: weakening `TR_BillingEntries_Immutable` (51120) or `TR_CompletedProcedureAmendments_Immutable` (51146) to a `PRINT` in a throwaway edit makes the tooling fail (2 of 2 and 1 of 2 failed); the migrations were restored with `git checkout`.

The attack harness repeats the Gate C one (`GateCHistoryIntegrityTests`, approved and left unchanged).
