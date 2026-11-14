# ALV-N021 briefing R02 — owner decision addendum 3: checkout-readiness gate

**Date:** 2026-11-13  
**Applies to:** ALV-N021 briefing R02 and owner-decision addenda 1–2.  
**Status:** Binding direction for the next briefing revision. Do not implement from this document alone.

## Owner direction

A visit must not become `Completed` merely because a user presses Complete. After the treating provider performs **End treatment / ready for checkout**, the server must evaluate whether every applicable checkout responsibility has a recorded resolution. Only then may an otherwise authorized front-office actor or qualifying treating dentist make the final `CheckedOut → Completed` transition.

There are no completion shortcuts and no generic unchecked bypass. A user who cannot complete must receive a clear, accessible list of the unresolved checkout items and the responsible role/action.

## Required checkout outcomes

The revised briefing must design a server-side, auditable checkout-readiness policy with these outcomes. An item is resolved only by choosing one of its permitted outcomes; omission is not a resolution.

| Checkout responsibility | Required when | Permitted outcome | Responsible actor |
|---|---|---|---|
| Treatment ended | Always | The visit has a recorded provider End-treatment event | Treating provider |
| Patient departure | Always | Patient departed/left the practice, with actor and time | Front-office actor; a separately authorized actor only if the final matrix explicitly grants it |
| Follow-up disposition | Always | Follow-up booked; no follow-up clinically required; patient declined; or deferred with a reason | Treating provider may select “not clinically required”; front-office actor may record booked, declined, or deferred; exact delegated permissions must be explicit |
| Financial disposition | When the visit has an applicable charge, balance, or payment workflow | Payment recorded; insurance/claim responsibility recorded; payment arrangement recorded; statement/balance follow-up recorded; or an authorized zero/waived/no-charge outcome | Financial/front-office authority defined by the existing permission model; never inferred from a clinical permission |

The financial outcome is a **disposition**, not a false claim that every visit was paid. For example, an insurance claim or documented payment arrangement may truthfully resolve the checkout task while the underlying balance remains open. Later financial-integrity work must connect this rule to the authoritative ledger and payment records; ALV-N021 must not fabricate a payment, charge, ledger row, claim, waiver, or balance.

## Completion behavior

1. `CheckedOut → Completed` calls one shared server-side readiness evaluator in the same transaction as the state move.
2. If any applicable item is unresolved, return a documented conflict response such as `checkout_incomplete` containing only safe item codes and user-facing next-action text. Write no completion event or audit entry.
3. If all items are resolved, either authorized final actor may complete under the rules in addendum 1. The dentist’s ability to complete does **not** allow the dentist to bypass financial or front-office work.
4. Concurrent resolution/completion requests use the appointment row version. Exactly one final completion can succeed; the loser sees the fresh outstanding/completed state with no duplicate history.
5. The history/audit records each checkout outcome's actor, time, prior/new value, and reason where required. It remains append-only; a later correction is a new event, never an edit to the original outcome.
6. Calendar and Visit Board obtain the same readiness result from the server. They must state, for example, “Ready for checkout — financial disposition required” rather than merely “Awaiting front desk,” when that is the real blocker.

## Scope and proof requirement

This is a material expansion of ALV-N021. The resubmitted briefing must state the exact entities/fields, permissions, APIs, UI workflow, migrations, legacy-data behavior, accessibility behavior, audit/history model, and recovery/correction route before code begins.

At minimum, its focused evidence plan must prove: every required/optional applicability case; denial of completion with each unresolved item; every permitted disposition; role denial; no fabricated financial record; dentist completion after all items are resolved; concurrency between resolution and completion; append-only correction history; Calendar/Board parity; and accessibility of unresolved-action guidance.

No broad regression or chain run is authorized by this decision.
