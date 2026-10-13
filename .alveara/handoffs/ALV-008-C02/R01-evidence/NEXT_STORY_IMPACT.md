# ALV-008-C02 R01 - Next-story impact

The next scheduled item is 37 `STORY-016` (prescriptions with medication, dosage and allergy checks), then the Gate D review (after item 38). Nothing of item 37 was started.

## For STORY-016 and later stories
- **Migration trigger numbers 51173 and up are free** (completion now owns 51144-51156 and 51172, billing 51118-51143 and 51157-51171).
- **No production path completes a procedure without a record any more.** Anything that reads "a completed procedure" can rely on the authoritative record for every procedure completed from now on; the only completions without one are the legacy ones still waiting for adoption, which `GET api/completion/reconciliation` counts (`legacyCompletedNotAdopted`). A feature that needs the provider, encounter or performed time of a completed procedure must treat a legacy completion as "unknown until adopted", exactly as the clinical history and the timeline already do.
- **`TreatmentPlanService` no longer has `CompleteItemAsync` or `CompleteAcceptedAsync`.** A story that wants a plan procedure completed uses `CompletedProcedureService.CompleteAsync` (or `AdoptAsync`); the test base classes' `LegacyComplete*` helpers exist only to create a starting state in tests.
- **Prescriptions and the chart.** Nothing in this story changes the odontogram contract; adoption moves a finding to Completed through the same `StageCompletedAsync` the completion workflow uses, and the reconciliation reads findings' version history (`ToothFindingVersions`), so a story that adds new finding states or version change types must keep "the latest version at or before an instant" meaningful.

## For Gate D (after item 38)
- **D1 (reconciliation).** `GET api/completion/reconciliation` is the Gate D evidence for "zero orphaned charges, zero missing charges among authoritative/adopted completions, matching odontogram"; a Gate D run on a database that still holds legacy completions will correctly report them as `legacyCompletedNotAdopted` until they are adopted. The walkthrough's output is in `ACCEPTANCE_EVIDENCE.md`.
- **Issued documents and adoption.** A statement or receipt issued before an adoption reprints exactly as issued (the fix described in R01.md); a story that writes anything else an issued document reads must make the same guarantee (the snapshot ignores what was finalized after the document's instant).

## Cautions
- The adopt form defaults the performed time to now; a future change that defaults it to the legacy completion's recorded time is UI-only (see Unresolved questions in R01.md).
- The 410 stubs can be deleted after a sunset period; `RetiredCompletionCallerSearchTests` pins their exact shape and would need its expectation of three hits changed to zero in the same commit.
