# ALV-007-C02 — Design Review Response

**Decision:** APPROVED WITH CHANGES  
**Reviewed proposal:** ALV-007-C02_DESIGN_PROPOSAL.md  
**Date:** 2026-10-12

The proposed append-only refund, release, correction, and derived-document approach is approved subject to the binding controls below. They have been added to Item 35 of the Engineering Execution Plan and govern implementation.

## Decisions

| Decision | Disposition | Binding outcome |
|---|---|---|
| D1 — Refund model | Approved | Use a Refund entry linked to its payment and explicit immutable allocation-release rows. |
| D2 — Refund policy | Approved with change | Refund only the original recorded payer (or account patient when no distinct payer was recorded), use unapplied-first then newest-allocation release, never create credit, and forbid refund/reversal of the same payment. |
| D3 — Allocation correction | Approved with change | Use release-plus-new-allocation atomically; canonicalize rows and reject invalid, duplicate, cross-account, wrong-payment, unequal, and over-release requests. |
| D4 — Replacement and re-billing | Approved with change | Reverse payment/adjustment and create linked replacement atomically. A replacement payment is unapplied; charge re-billing remains out of scope. |
| D5 — Document model | Approved with change | Use cut-offs, hash, precise AsOfUtc, template version, and immutable non-financial display envelope. Issuance must be bound to a server-generated preview token. |
| D6 — Print/export | Approved | HTML print styles and statement CSV only; no PDF library. |
| D7 — Statement scope | Approved with change | One numbered statement per account, cut-off only; preserve historically resolved recipient identity for reprint. |
| D8 — Aging | Approved with change | Live aging may use current completed-procedure amendment; issued-document aging uses the amendment effective at its own AsOfUtc. |
| D9 — Permissions | Approved | ViewBilling for reads and ManageBilling plus CSRF for writes; no new permission. |
| D10 — Reasons and attribution | Approved with change | Keep free-text reasons and full actor/time attribution; refund recipient is constrained to the original payer. |

## Required document behavior

An issued document is never an alternate balance source and stores no monetary values or balance snapshots. Its printable/exportable content is the exact as-issued canonical content. Later corrections may appear as a clearly separate current-status notice in the UI, but must not alter the issued representation or its hash.

If ledger, identity, responsibility, or relevant completed-procedure amendment history changes between preview and issue, issuing the reviewed preview must either reproduce that exact canonical content or fail with document_preview_stale. Reprint/export must fail safely with document_content_changed if canonical reconstruction does not match.

## Required evidence

Include deterministic stale-preview and concurrent-operation tests, a cent-exact reconciliation set of at least 50 transactions spanning refunds, releases, corrections, and moves, and tests for historical responsibility, issued-document aging, reprint hashing, original-payer refunds, atomic rollback, and all existing billing/completion regression suites.

Do not start implementation until these binding decisions are reflected in the local plan, which is now updated.
