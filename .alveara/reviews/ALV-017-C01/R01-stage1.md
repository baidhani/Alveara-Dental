# ALV-017-C01 — Independent Stage 1 Review R01

**Decision:** APPROVED_PENDING_REGRESSION  
**Reviewed:** 2026-10-25  
**Handoff:** `Alveara_Handoff_ALV-017-C01_5f0efb5_R01.zip`  
**Package SHA-256:** `4A0CB29663FA423162466FF246D0F33B5CF08BA7E4C24AF95CB5EDFDDACA9AFA`  
**Implementation reviewed:** `5f0efb5a443a30760a267844270c1b5e60c87aa5`

## Scope of this review

This is a Stage 1 decision under the two-stage handoff policy. It authorizes one frozen Stage 2 regression after a review-closure-only commit. It is not final approval and does not authorize STORY-018.

## Evidence reviewed

- Portal and repository control state: STORY-017 is portal-verified complete with all 3 criteria passed; the companion begins from its documented completed baseline.
- Repository history, package manifest, implementation source scope, and a clean working tree at package time.
- Evidence for all six acceptance conditions: parent regression; explicit human confirmation; cross-domain clinical/financial/document/task preservation; conflict resolution; transactional recovery and safe undo; and visible lineage.
- Focused backend TRX: **3,076/3,076** passed. HTTP-level `*ApiTests` TRX: **883/883** passed.
- Test identity derivation: 52 new IDs in both the changed test surface and assembly discovery; one baseline ID explicitly retired because its test was renamed and expanded from four to nine protected endpoints. The frozen total is therefore **5,131** = 5,080 baseline − 1 retired + 52 new.
- Client and repository gates: TypeScript, build, oxlint, mocked Playwright (140), and repository checks (13) passed; Vitest passed **1,676 tests in 105 files**.
- Real-backend evidence: patient merge 9, workspace 14, patients 7, permission policy 7, and permission-policy administration 8 all passed.
- Negative controls: all **38/38** final mutations were killed. The two initial survivors drove new guard tests and were re-run successfully; source restoration is documented.
- Frozen evaluator validation: **9/9** expected acceptance/rejection scenarios passed.

## Disclosures assessed

The three new `react(set-state-in-effect)` warnings do not fail the configured oxlint gate and are explicitly recorded. One pre-existing scratch `AlveraTest_...` database is reported rather than silently deleted. This follows the project’s artifact policy; the Stage 2 run must retain its own before/after no-growth check. Neither disclosure blocks Stage 1 approval.

## Required next action

1. Record this decision in a review-closure-only commit. Do not alter product or test source after that commit.
2. Run **one** frozen Stage 2 regression from that closure commit on a clean tree.
3. The regression must establish exactly **5,131 passed, 0 failed, 0 skipped** backend tests: the 5,080-ID STORY-017 baseline, less the one declared renamed/retired ID, plus exactly the 52 allowlisted new IDs. It must also meet the frozen template, cleanup, gate, and walkthrough requirements.
4. Submit the regression ZIP for final review. Do not start STORY-018 until the final decision is `APPROVED` and its closure is recorded.
