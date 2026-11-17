# ALV-N022 A2 — Step 0 / Addendum 02 Review

**Document reviewed:** `docs/design/ALV-N022_A2_STEP0_AND_ADDENDUM_01.md`, section 7 (Addendum 02)  
**Prior decision:** Addendum 01 `CHANGES_REQUIRED` — completion-attribution authority conflict  
**Decision:** **CHANGES_REQUIRED — two small evidence-contract corrections; no implementation yet**

## Resolved

Addendum 02 correctly records the owner’s 2026-11-17 decision and reconciles it with the binding ALV-008-C01 completion contract:

- diagnosis creation, and any future diagnosis re-link, refuse a Finalized encounter before a write;
- completed-procedure completion, adoption, and append-only amendment may use a same-patient Draft or Finalized encounter as attribution and do not modify that encounter;
- document and referral links remain unchanged in A2;
- signed Draft behavior remains unchanged; and
- the unreliable generic “attached after finalization” display is correctly deferred rather than invented.

The proposed planning-record amendments are appropriately explicit.  They must be applied in the approved document-only update before A2 code begins.

## Remaining corrections

### CR-1 — restore the required real-browser clinical proof

Execution Plan item 59 requires real-browser clinical-workflow coverage for this clinical-integrity package.  Addendum 02 section 7.6 has no browser row.

Add one narrow real-backend Chromium workflow to A2’s focused contract.  It must prove, without any full suite:

1. a diagnosis form for a patient with both Draft and Finalized encounters offers only the Draft encounter and gives the reviewed finalized-encounter message; and
2. a completion/adoption picker still presents both same-patient Draft and Finalized encounters, with status visible, and a completion attributed to the Finalized encounter succeeds without changing that encounter’s status, version, or history.

It may use a purpose-built fixture/API setup and need not cover documents or referrals.  Expected duration must be stated; if it exceeds five minutes, request owner permission before running it.

### CR-2 — state the required two-stage route

The same plan item requires two-stage review.  Addendum 02 must explicitly state:

- **Stage 1:** implementation handoff with the focused server/client/browser proof; reviewer examines the frozen implementation and evidence;
- **Stage 2:** only after Stage 1 approval, run the reviewer-frozen targeted contract on the unchanged implementation, then return the regression/closure evidence; and
- broad/full regression, chain, and unrelated walkthroughs remain deferred to ALV-N028.

The Stage 2 contract must be defined by the reviewer after seeing the A2 implementation.  Do not pre-run it and do not convert it into a broad regression.

## Required Addendum 03

Return one document-only Addendum 03 that adds CR-1 and CR-2, then apply the already proposed document-only amendments to the owner decision record, briefing section 8, briefing section 10 A2 row, and execution-plan item 59.  Package that documentation update for independent approval.

No production code, migration, external integration, broad/full test, chain, or A2 implementation is authorized until that package is approved.
