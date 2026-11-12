# Manual E2E Stabilization Plan R04 — Independent Review

Date: 2026-11-09

Decision: **CHANGES_REQUIRED**

Package: `C:\.alvera-data\Manual-E2E\alveara-manual-e2e-reviewer-package-v5-2026-11-09.zip`

SHA-256: `3C5DFE6D3162E757C9DD896A10C7809292A83FC4AED5D85BB1F0FCEDC0E1FE58`

Reviewed plan: `16-STABILIZATION-PLAN-R04.md`.

## Corrections accepted

The prior mapping examples now identify meaningful owners and assertions. Lines 255 and 385 are corrected. For line 472, retaining the Charting stage is acceptable because R04 explicitly identifies diagnosis-record and permission coverage; the stage label itself is not a blocker. Steps 118–119 now distinguish the F-170 acceptance owner from the packages building the relevant chain stages.

Direct comparison confirms sections 0, 3, 4 and 5 are unchanged from the packaged R03: release sequence, package scope/order and owner decisions were preserved. Section 6.1 now contains concrete expected outcomes instead of the repeated generic explanation. Section 12 records the mapping corrections.

This is a plan review, not certification that the automated tests exist or pass. No test run is requested for these documentation corrections.

## Remaining required corrections

### 1. Resolve Q-5cl in the automated coverage map

Section 6.3 still labels Q-5cl `not placed`, `pending`, and `to be decided with the second pass`, assigning it to PKG-16. The raw log states the actual questions: whether manager1 has a read-only or editable Permission Matrix, and which documents its Documents tab exposes, including clinical documents.

Assign these to the permission and document coverage owners (PKG-10 and PKG-12, with PD-24 where applicable). Specify UI and direct-API assertions against the approved permission policy. If clinical-document visibility awaits PD-24, record that dependency explicitly. A missing policy answer may remain an owner decision; the test responsibility and scenario must already be defined. PKG-16 should verify completed coverage rather than discover the scenario during a later manual pass.

### 2. Preserve browser-level coverage where lower-level tests are not equivalent

- Case 1.10 substitutes a component test for signing out and pressing the browser Back button. Add real-browser coverage of sign-out followed by Back, asserting that protected content and cached patient information are not displayed. Component history tests alone do not establish actual browser history/cache behavior.
- Case 2.09 substitutes concurrent API requests with a supplied common idempotency key for double-clicking Register. Keep the API test, but also exercise the real UI submission path and prove one patient is created. Supplying the same key in a test does not prove that the UI reuses the key or suppresses duplicate submissions.
- Apply this equivalence check to the other browser-to-API/component substitutions. API concurrency is useful for reliably exercising races; retain browser assertions for UI behavior that the lower-level test cannot observe. Existing real-browser tests can satisfy these obligations if explicitly mapped; do not duplicate adequate coverage.

## Editorial cleanup

Section 7.D.8 still says the mapping is keyword-based, contradicting sections 1 and 12.6. Correct it to accurately describe the R04 method and its limits. Also rename the section 1 heading from response to R02 to response to R03. These editorial issues alone would not block approval.

## Next submission

Submit an immutable R05 containing these focused coverage corrections and a short change log. Preserve the accepted package structure and release sequence. No full test run is required to review this planning revision. Implementation remains subject to the authoritative execution-plan/control update after plan approval, followed by the existing two-stage review policy.
