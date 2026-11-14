# ALV-N021 A1-R01: next-story impact

- **A2 (same story, next attempt):** may start only after A1 is approved and closed (BRIEFING-R04). A2 reuses `components/dateOnly.ts` for any date-only value it shows and `clock.ToPracticeLocal` for any practice day it names on the server (check-in day rule, Week view).
- **Billing documents:** new statements and receipts are template version 2. Any later change to statement content must add a version 3 and keep versions 1 and 2 rebuilding byte for byte; the baseline fixture and `FinancialDocumentTemplateVersionTests` guard version 1. A version-2 baseline is not yet captured; the next change to the builder should capture one first, as A1 did for version 1.
- **PKG-14 (date-format preference):** the classification lists every client date site by class; the statement view's service dates follow the viewer's zone (recorded, not changed).
- **ALV-N028 (broad regression):** owes the rows added to the ledger for A1.
- **Treatment-plan API:** `PlanItemView` gains two optional fields (`answerRecordedByName`, `answerRecordedAtUtc`); additive, existing fields unchanged in meaning except that `decisionRevision` is now the revision of the answer row (before: of the latest row).
