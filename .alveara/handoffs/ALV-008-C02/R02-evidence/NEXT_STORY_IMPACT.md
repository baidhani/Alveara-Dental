# ALV-008-C02 R02 - Next-story impact

The next scheduled item is 37 `STORY-016`, then the Gate D review. Nothing of item 37 was started. Everything in `../R01-evidence/NEXT_STORY_IMPACT.md` stands, with these changes:

- **Migration trigger numbers 51174 and up are free** (this attempt used 51173 as well as R01's 51172).
- **A completed procedure that names a chart finding now carries `ToothFindingVersionNumber`.** Anything that inserts a `CompletedProcedures` row directly (tests, seeds, a future import) must name an active Completed version of that finding that is not newer than the completion, or the database refuses it (51173). The product paths do this themselves.
- **A story that adds new chart history version kinds or finding states must keep "an active Completed state" meaningful**: the evidence check and the read both test `State = Completed` and `Status = Active`.
- **Gate D (D1, reconciliation).** `GET api/completion/reconciliation` is now exact: it reports a completion only when its recorded evidence is missing or wrong, and a later legitimate chart change, even at the same practice-clock instant, cannot cause a report. A Gate D run on a database with older completions relies on the migration's one-time backfill (documented in `docs/COMPLETION.md`); any older completion it could not derive is reported as a mismatch.
- **Regression cadence.** Per the cadence record, the baseline for "last successful broad regression" becomes this story only after R02 is approved and durably closed; it is not written into BUILD_STATE.md before then.
