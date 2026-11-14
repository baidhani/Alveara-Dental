# ALV-N021 briefing R02 — owner decision addendum

**Date:** 2026-11-13  
**Applies to:** `Alveara_Briefing_ALV-N021_cb7866b_R02.zip`, briefing revision 2, and review `BRIEFING-R02.md`  
**Status:** Binding owner clarification for resubmission; it does not alter the immutable R02 review record.

## Final visit completion: two valid authorized actors

The owner confirms that either of the following actors may perform the one final transition from `CheckedOut` / **Ready for checkout** to `Completed`:

1. A front-office actor holding `UpdateVisitFlow` (FrontDesk, OfficeManager, or Admin under the approved default matrix); or
2. The treating dentist holding `CompleteOwnStartedVisit`, **only** when the server proves that same account created the appointment's recorded `InTreatment` event.

These are alternative authorized actors for **one** administrative-completion transition. They are not two sequential completion steps and must use the same server-side transition policy, row-version concurrency check, terminal-state rule, event/history format, audit record, and idempotency behavior.

## Required wording and UI consequence

Do not universally state “awaiting front desk” after treatment ends. The available-actions response and UI must truthfully describe who may next act:

- for a visit eligible for the treating dentist's completion privilege: **“Ready for checkout — front desk or Dr. [name] may complete the visit.”**
- for every other visit: **“Ready for checkout — awaiting front desk completion.”**

Calendar and Visit Board must derive that wording and the visible Complete action from the same server result. If both actors submit the final action concurrently, exactly one succeeds; the other receives the documented stale/terminal response, and only one completion history/audit entry exists.

## Scope note

The owner explains that the package's proposals were developed through direct discussion with the coding agent and most were owner-requested. The independent reviewer must evaluate their implementation contract and evidence, rather than treating the agent's presentation alone as authority. No execution-plan or status-ledger change is made by this addendum.
