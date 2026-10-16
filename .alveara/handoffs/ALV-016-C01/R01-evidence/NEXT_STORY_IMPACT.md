# ALV-016-C01 R01 - Next-story impact

**Next scheduled item: GATE D (Treatment Completion and Financial Core Ready)**, then item 39 `STORY-010` (a portal course story) and item 40 `ALV-010-C01` (documents and imaging). This attempt stops at `AWAITING_REVIEW`; the review closure must be recorded before Gate D starts.

## For Gate D
- **D4 (prescription workflow consumes the shared safety context and does not claim unsupported medical dose decision support)** is now evaluable. Evidence to inspect: `R01-evidence/ACCEPTANCE_EVIDENCE.md` criteria 4 and 5; the walkthrough `01-composer.png` (allergy, medication and alert shown; the "Not configured" banner), `05-allergy-match-needs-review.png` (the allergy check fires), `03-confirmation.png` (the not-checked statement); `docs/PRESCRIPTION_WORKFLOW.md`; the code (`PrescribingSafetyReader` reads only ALV-N011's `ISafetyContextProvider`; no method of the workflow is named for interactions or appropriateness, pinned by a test). `QUALITY_GATES.md` was not edited: D4 is recorded by the Gate D run itself.
- **Duplicate submissions, corrections and stale edits survive without destructive overwrite** (the last Gate D bullet) for prescriptions: replay-safe create, amend and finalize; a stale draft edit is `concurrency_conflict`; nothing finalized is ever edited (it is amended as a new record); proofs in `ACCEPTANCE_EVIDENCE.md` criterion 6.
- **Gate D's "last successful broad regression" baseline.** This attempt ran the full backend, frontend, repository and mocked-browser regression (`TEST_RESULTS.md`). `BUILD_STATE.md` records it as the baseline only after approval and closure, as the cadence response requires, so it does not yet.
- **A test to watch.** `CompletedProcedureServiceTests.Two_overlapping_sets_submitted_at_once...` (ALV-008-C01) failed once as a deadlock victim in the full run (details in `HANDOFF.md`). It is intermittent; Gate D's "financial and completed-procedure histories survive duplicate submissions" evidence should expect it and may want the completion service to retry a deadlock victim.

## Interfaces and conventions now available
- **Triggers:** 51175 to 51181 are used; **51182 and up are free**. Migration chain tip: `20261014024801_AddPrescriptionWorkflow`.
- **Audit event names:** `PrescriptionDraftCreated`, `PrescriptionDraftChanged`, `PrescriptionDraftDiscarded`, `PrescriptionDraftSafetyAcknowledged`, `PrescriptionAmendmentStarted`, `PrescriptionRecorded` (also written by STORY-016's route), `PrescriptionSuperseded`, `PrescriptionCancelled`, `PrescriptionDiscontinued`, `PrescriptionPrinted`.
- **Reusable pieces:** `PrescribingSafetyReader` (the allergy and medication lists from the shared safety context, with a fingerprint) and `SafetySnapshot` (a stable snapshot and a readable difference) can serve any future workflow that must notice that a patient's lists changed; `PrescriptionStatuses.From(events)` shows the status-from-events pattern; `PrintPreview` and `PrescriptionPrint` follow the billing documents' server-built, hashed, logged print pattern.
- **Permissions:** none added (`ViewPrescriptions`, `ManagePrescriptions`).
- **UI extension points:** the workspace tab `prescription-composer`; `PrescriptionWorkflowPanel` takes `patient` and `canPrescribe`.
- **Prompt text and conventions unchanged:** no change to the next items' prompts is needed. Items 39 and 40 (documents and imaging) do not depend on prescriptions; a prescription print is the browser's print of server-built content and is not stored as a document, so an "import and categorize documents" story would not see it unless the product later decides to file prints.

## Assumptions the reviewer may revisit
- A second tab beside STORY-016's quick entry (STORY-016's contract is immutable); retiring the quick form needs STORY-016's tests rewritten.
- The quantity ceiling (1000) and refill ceiling (12) are typing-error ceilings, not clinical or legal limits.
- The configured policy for an allergy conflict is STORY-016's review-note rule (no separate configuration surface).

## Unresolved decisions
- Whether to retire the quick entry form (above) and whether the completion service should retry a deadlock victim (see `HANDOFF.md` "Unresolved questions").
