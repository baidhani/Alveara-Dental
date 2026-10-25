# ALV-009-C02 — Independent Stage 1 Review R01

**Decision:** APPROVED_PENDING_REGRESSION  
**Reviewed:** 2026-10-24  
**Handoff:** `Alveara_Handoff_ALV-009-C02_36f817e_R01.zip`  
**Package SHA-256:** `07F32D73B645217C42F4CA859EF466FDB57120B57FF7E3B1AB289D54C90DC1CB`  
**Implementation reviewed:** `36f817efaeced07cbcfa718c5ba3170d7b263749`

## Scope of this review

This is a Stage 1 decision under the two-stage handoff policy. It authorizes one frozen Stage 2 regression after a review-closure-only commit. It is not final approval and does not authorize STORY-017 or its companion work.

## Evidence reviewed

- Repository history, package manifest, and a clean worktree at package time.
- Source scope from the completed ALV-009-C01 closure: referral and laboratory case aggregates, migration and database guards, case/party services and API, durable follow-up jobs, permissions, patient/work-queue/client surfaces, real-backend walkthrough, and the supporting tests.
- Acceptance evidence for all four required outcomes: referral lifecycle, lab lifecycle, linked documents, and overdue queue visibility.
- Focused backend suite: **1,606/1,606**; HTTP-level `*ApiTests`: **872/872**; restore drill: **1/1**.
- New-test identity derivation: **117** new IDs, cross-checked against `dotnet test --list-tests`; no changed-file test class absent from the runs, no baseline ID missing or outcome changed in covered classes.
- Targeted quality evidence: TypeScript, Vitest (**1,647 in 103 files**), build, lint, mocked Playwright (140), and repository checks (13) passed.
- Real-backend walkthroughs passed: external cases 9, work queue 8, follow-ups 8, notifications 10, permission policy 7, and permission-policy administration 8.
- Negative controls: all **33/33** required controls were killed. The two initial document-permission survivors resulted in an additional controller-permission test; both then failed under the intended mutation.
- Frozen evaluator validation: **9/9** scenarios behaved as required.

## Disclosure assessed

One pre-existing `AlveraTest_...` scratch database is disclosed and is not auto-deleted. This follows the established artifact policy: leftovers are reported rather than silently removed, and the Stage 2 contract compares its own before/after counts. It does not block this Stage 1 approval. The Stage 2 run must preserve the frozen no-growth requirement.

## Required next action

1. Record this review in a review-closure-only commit. Do not modify product or test source after that commit.
2. Run **one** frozen Stage 2 regression from the closure commit, on a clean tree, using the approved R01 evaluator.
3. The regression must establish exactly **5,023 passed, 0 failed, 0 skipped** backend tests: the 4,906-ID ALV-009-C01 baseline plus exactly the 117 allowlisted new IDs. It must also meet the frozen template, leak, quality-gate, and walkthrough requirements.
4. Submit the resulting regression ZIP for final review. Do not start STORY-017 or another companion item until that final review is `APPROVED`.
