# ALV-N018 — Independent Review R01, Stage 1

**Decision:** CHANGES_REQUIRED  
**Reviewed:** 2026-10-23  
**Handoff:** `Alveara_Handoff_ALV-N018_41c44ab_R01.zip`  
**Package SHA-256:** `FFA12D5C2D05966450DA8F106C76F6BD9353DEF41F31CBA1861220C411776761`  
**Implementation reviewed:** `41c44abf671ba869e60beeb9bc496f7f2d46034c`  
**Evidence reviewed:** `effc0a51bbb0e114bc75e15f7e9bfa1341f14fc0`

## Ruling

The package is not approved for Stage 2. It has a material mismatch between a mandatory acceptance proof and the submitted real-backend evidence.

### ALV-N018-R01-01 — Refresh proof permits 45 seconds, not the required 30 seconds

Item 44 requires proof of refresh **within 30 seconds**. The submitted walkthrough explicitly waits up to 45 seconds in `src/alveara-client/e2e/permission-policy-admin-real-backend.spec.ts` and writes `withinSeconds: 45` into `permission-policy-admin-evidence.json`. The handoff then maps that evidence to the mandatory 30-second requirement.

The present client timer is also exactly 30,000 ms. A change made immediately after a polling tick can therefore take nearly 30 seconds before the request begins, and ordinary request/render time follows it. That does not establish a 30-second end-to-end ceiling.

**Required correction:**

1. Poll with sufficient margin below the requirement, such as 25 seconds, while preserving the existing safeguards that do not refresh while an editor is open or while the document is hidden.
2. Update the real-backend walkthrough to measure the elapsed time from the other-session write to the visible refreshed state and fail at 30 seconds or less. Do not retain a 45-second timeout or report `withinSeconds: 45` as qualifying proof.
3. Keep the unit test aligned with the revised interval and regenerate the walkthrough evidence, focused client tests, targeted backend/API evidence as appropriate, and the Stage 1 handoff as a new immutable attempt.

Do **not** run the full Stage 2 regression for R01. It is not authorized because Stage 1 has not passed.

## Evidence independently checked

The following portions were substantively present and correctly scoped, subject to the required correction above:

- Server-side write paths require CSRF, idempotency keys, policy-version concurrency, bounded reasons, confirmation, and typed confirmation for the defined high-impact permissions. Writes are transactionally coupled to history and audit rows.
- Protected permissions and fixed roles are rejected by the write service; delegation remains a closed Office Manager table with the five permitted target roles and no delegation of the two assignable account controls.
- The broadened user-list route uses an any-permission server attribute and preserves the former `ManageUsers` denial body for callers with neither assignable permission. The targeted tests cover Admin visibility and the non-Admin subset rule.
- The Stage 1 package correctly leaves full regression pending and contains a frozen Stage 2 contract. Its 45-second refresh allowance is the only identified acceptance-blocking defect in this review.

## Resubmission instructions

Submit `ALV-N018` R02 as a new Stage 1 handoff after the correction. It must remain `AWAITING_REVIEW` and `fullRegression: PENDING`. Include the corrected measured refresh evidence and make its acceptance mapping state the actual measured result. No Stage 2/full regression should be started unless R02 receives `APPROVED_PENDING_REGRESSION`.

