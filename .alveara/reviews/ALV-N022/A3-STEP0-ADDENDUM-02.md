# ALV-N022 A3 — Step 0 / Addendum 02 Review

**Document reviewed:** `docs/design/ALV-N022_A3_STEP0_AND_ADDENDUM_01.md`, Addendum 02, commit `c302cb0`  
**Decision:** **APPROVED — A3 Stage 1 implementation is authorized within the bounded scope below**

## Basis for approval

Addendum 02 resolves every item from the prior Step 0 review:

- It records the owner's three decisions: typed nullable links with foreign keys for F-090/F-098, F-092 as `NOT_REPRODUCED`, and only the neutral Q-5aj completion-result sentence.
- It restores F-091's required non-semantic presentation rule: the accessible absent mark is shown only for an active `toothEffect = Absent` finding in `Existing` or `Completed`; Diagnosed/Planned findings retain their recorded status without that mark.  It does not infer extraction intent or an implant/missing relationship.
- It defines durable clearance-document and periodontal-diagnosis links: separate nullable ID columns, foreign keys, service validation, database same-patient triggers (`51288`, `51289`), preserved legacy text, and a rollback that refuses to discard populated typed links.
- It includes migration, direct-persistence, legacy-display, negative-control, and fresh-database real-browser evidence.  The browser workflow is expected to take 1.5–2 minutes, which is within the owner's standing focused-test permission.
- It preserves the clinical boundary: no medication-reference lookup, allergy-class list, interaction, dose, compatibility, clearance, or prescribing-decision behavior is authorized.

## Authorized A3 Stage 1 scope

Implement only the A3 items and exact migration/evidence matrix in Addendum 02, including F-106, F-105 wording guard, F-067, F-082, F-090, F-091, F-093–F-099 as listed, F-106, F-137, F-199, Q-5aa, Q-5ai, Q-5j, and the neutral Q-5aj sentence.  F-079, F-089, and F-092 remain `NOT_REPRODUCED`; build nothing for them.

The new clearance/document and periodontal/diagnosis database triggers are authorized only for their stated same-patient invariants.  Do not use this work to change finalized-encounter, completed-procedure, missing-tooth, prescription, or clinical-decision rules.

## Evidence and review route

1. Run focused Stage 1 proof only: the listed targeted server/client/migration tests and the single fresh-database real-backend Chromium workflow.  Stop and request permission if any group is expected to exceed five minutes.
2. Package the Stage 1 handoff at `AWAITING_REVIEW` with exact commands, test IDs/results, actual durations, migration up/down/direct-persistence evidence, negative-control results, browser evidence, and an explicit changed-files inventory.
3. Do **not** run a broad/full regression, a chain, or any other long suite.  Set `fullRegression: "DEFERRED_TO_ALV-N028"`.
4. Stage 2 is not approved or frozen yet.  It will be defined only after the independent Stage 1 implementation review.  No A3 completion or closure is authorized by this document.

## Record disposition

Record F-092 as `NOT_REPRODUCED` using the Step 0 source evidence.  Reopen it only upon the raw source entry or a reproducible screen.  Preserve all source-linked records and legacy free-text values unchanged.
