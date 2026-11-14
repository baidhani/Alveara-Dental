# ALV-N021 briefing R02 — owner decision addendum 2: no shortcuts and checkout meaning

**Date:** 2026-11-13  
**Applies to:** ALV-N021 briefing R02 and owner-decision addendum 1.  
**Status:** Binding clarification for the next briefing revision.

## No completion shortcut

There is **no shortcut** to completion. A visit may become `Completed` only after the treating provider has performed **End treatment / ready for checkout** from `InTreatment`.

The following remain forbidden and server-refused: `CheckedIn → InTreatment`, `CheckedIn → Completed`, `Seated → Completed`, and `InTreatment → Completed`. A front-office actor and the qualifying treating dentist are alternative actors only for the final `CheckedOut → Completed` transition; neither may skip the prior treatment-ended transition.

## Meaning of “checkout” in this workflow

“Checkout” is the post-clinical, administrative close of the visit: for example, final desk processing, collecting or recording payment where applicable, arranging the next appointment, and confirming that the patient has left. It is not a clinical treatment step.

For compatibility, the persisted legacy state name remains `CheckedOut`, but the user-facing state after the provider ends treatment is **Ready for checkout**. It means clinical work has ended and the visit is eligible for final closure; it must never be presented as proof that front-desk checkout already occurred.

If the qualifying treating dentist completes the visit at that point, the action closes the visit record only. It does not fabricate a payment, financial posting, future appointment, or other front-desk transaction; those remain their own explicitly recorded workflows.
