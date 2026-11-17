# ALV-N022 A2 — Step 0 / Addendum 01 Review

**Document reviewed:** `docs/design/ALV-N022_A2_STEP0_AND_ADDENDUM_01.md`  
**Decision:** **CHANGES_REQUIRED — authority conflict; no implementation authorized**

## What the survey establishes

The Step 0 survey is useful and sufficiently specific.  It correctly identifies every currently known production path that creates or changes an `EncounterId`, distinguishes client pickers from API-only paths, and discloses the existing development-data timing counts.  No code or database write occurred.

The following proposed choices are sound in isolation:

- **Q-1: choose A.** Document import/edit and referral cases may continue to link a finalized encounter.  They are records about a visit, not new clinical care, and their picker/filter behavior should not be broadened in A2.
- **Q-4: retain current signed-draft behavior.** Signed-but-not-finalized policy is expressly outside A2.

## Blocking conflict

The proposed A2 plan would refuse completion, adoption, and completion-amendment attribution when the selected encounter is Finalized.  That contradicts the approved ALV-008-C01 execution-plan decision:

> “The encounter must belong to the patient and may be Draft or Finalized; completion records attribution only and never changes a finalized encounter.”

The same approved decision limits completed-procedure amendments to append-only corrections of provider, performed time, and encounter attribution.  Therefore a correction may legitimately re-attribute a completed procedure to an appropriate finalized encounter.  A2 must not override that completed-procedure contract implicitly.

The phrase in the older A2 briefing, “every path that sets `EncounterId`,” is too broad.  It cannot be used to turn a valid completion attribution or append-only amendment into forbidden “new care.”  This is an engineering/product authority conflict, not a test gap.

## Required addendum revision

Return a document-only Addendum 02 that reconciles the authorities before any A2 code is written.  It must state the following path-specific rule:

| Path | Finalized encounter rule |
|---|---|
| Diagnosis creation and diagnosis re-link | Refuse with `409 encounter_finalized`; a diagnosis is new clinical documentation and later care belongs in a new draft encounter. |
| Completed-procedure completion and adoption | Allow a same-patient Draft **or Finalized** encounter as attribution, as already required by ALV-008-C01.  The completion must not mutate the finalized encounter. |
| Completed-procedure amendment / re-attribution | Allow an eligible same-patient Draft **or Finalized** encounter only through the existing append-only amendment contract; never rewrite the original completion or the encounter. |
| Document import/edit and referral create/update | Leave unchanged in A2 (Q-1 A); record the later decision need, but add no restriction or picker change. |

### Q-3: timing label

Do not implement the proposed generic “attached after the encounter was finalized” label in A2 until the authority wording is revised.  It would misleadingly characterize a valid, intentionally supported completed-procedure attribution as a defect, and exact link time is not available for all re-link paths.

The revised addendum should either:

1. remove the label from A2 and retain existing rows unchanged, or
2. define a neutral, path-specific historical display using an authoritative event timestamp only where one exists.

My recommendation is **option 1**: defer that display refinement.  It is not necessary to enforce diagnosis finalization and avoids inventing timing semantics.

## Test-plan consequence

The revised A2 evidence must prove both sides of the clarified boundary:

- diagnosis create/re-link: Draft accepted; Finalized refused before any write; picker excludes finalized encounters;
- completion/adoption and append-only amendment: same-patient Draft **and Finalized** attribution accepted without mutating the encounter; wrong-patient attribution refused; and
- documents/referrals unchanged and out of A2 implementation scope.

Keep signed drafts unchanged.  No broad/full regression, chain, migration, or implementation is authorized yet.

## Authority update required

Because OD-06 and briefing section 8 currently conflict with the approved ALV-008-C01 decision, Addendum 02 must also propose a precise amendment to those planning records.  It must be independently reviewed before A2 implementation.  Do not silently choose one conflicting authority in code.
