# ALV-N021 pre-implementation briefing review — R03

**Reviewed package:** `Alveara_Briefing_ALV-N021_da2db67_R03.zip`  
**Package SHA-256:** `ED37E1BEA408EEB90B51F34C69AB2BF1CD76A5C2D0783C5DB683AA44AA12363B`  
**Declared document commit:** `da2db67`  
**Date:** 2026-11-13  
**Decision:** **APPROVED_FOR_A1_ONLY. A2 and A3 are CHANGES_REQUIRED and are not authorized.**

## Approved scope: A1 only

The PKG-04 date/time attempt is independently scoped, carries no unresolved policy or schema decision, preserves the accepted full source appendix, and has an appropriate focused proof plan. The agent may implement **A1 only**: date-only/time integrity and its mapped PKG-04 findings.

The binding focused-test permission remains in force. Do not start a test expected to exceed five minutes, a chain/walkthrough, or a broad/full regression without the owner's explicit permission. Broad regression remains deferred to ALV-N028. A1 must return through the ordinary Stage 1 handoff review before any Stage 2/frozen evidence activity.

## A2/A3 blocking correction 1 — do not create a checkout-gate gap

The owner's rule is that a visit cannot become `Completed` while an applicable checkout responsibility remains unresolved. R03 stages this as A2 followed by A3, but table 5.4 makes checkout readiness applicable only in A3. That would let an A2 implementation ship the new final-completion path without the required server gate.

**Required resolution:** A2's visit-workflow release and A3's checkout-readiness gate must be one atomic production change and one reviewed implementation package, even if their code is built in substeps. The final `CheckedOut → Completed` route, its UI action, and all legacy completion endpoints must not be enabled, deployed, or accepted as complete until the readiness evaluator is present and server-enforced. Rename/restructure the attempts accordingly; there must be no period in which the new workflow permits completion without the gate.

## A2/A3 blocking correction 2 — completion authority must be limited to dentists

The owner decision and addenda name the **treating dentist** as the optional second final actor. Table 5.4 currently shows `g` for Hygienist and Assistant on Complete, allowing the new permission to be granted to either.

**Required resolution:** the server must require both a `CompleteOwnStartedVisit` grant **and** that the actor is a linked Dentist provider who recorded the `InTreatment` event. Hygienist and Assistant must remain denied even if a role/account is accidentally granted that permission. Update the permission table, policy-health contract, API authorization, and denial tests.

## A2/A3 blocking correction 3 — unsafe blocked-time overlap is undefined

OD-17 allows a confirmed block over appointments but says active/completed visits are “never touched.” That does not state whether the block is refused when it overlaps an active visit; it must not silently create an unavailable-provider/room condition on a patient already in care.

**Required resolution:** block creation must return a conflict and write nothing if it overlaps an appointment that is CheckedIn, Ready, Seated, InTreatment, or Ready for checkout/CheckedOut. The confirmed-and-flagged route may apply only to future, not-started Scheduled/Confirmed appointments. Add focused API, UI, concurrency, audit, and negative-control coverage.

## A2/A3 control correction — decision record approval

`ALV-N021_OWNER_DECISION_RECORD.md` shows an unchecked/unsigned approval line under the owner's name. It is useful as a proposed decision record but not evidence that every OD-01 through OD-37 has been personally approved.

For the next submission, either attach an actual owner approval/decision record, or label each undecided item as proposed and remove it from the authorized implementation scope. No execution-plan or status-ledger change for ALV-N030, ALV-N031, or ALV-N032 is authorized by this briefing review.

## What is accepted in principle

- The source-evidence appendix remains complete and verifiable.
- The no-shortcut sequence and the visible `Ready for checkout` terminology correctly address the manual visit-step findings.
- Server-derived Calendar/Board actions, row-version conflict handling, append-only checkout outcomes, and a read-only ledger evaluator are the right architectural direction.
- The checkout outcomes correctly distinguish a truthful financial disposition from fabricating payment or ledger data.

## Next handoff path

1. Implement A1 only and submit its Stage 1 handoff.
2. Revise and resubmit the combined workflow-plus-checkout briefing with the three corrections above before beginning any A2/A3 code, test, migration, or configuration change.
