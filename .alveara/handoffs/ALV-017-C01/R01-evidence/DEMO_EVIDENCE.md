# ALV-017-C01 R01 - Demo evidence

A real browser against the real API and a fresh database (`scripts/Invoke-RealBackendWalkthrough.ps1`, `playwright.patient-merge.config.ts`): **9 of 9 steps**. Screenshots and the machine-readable record are in `artifacts/walkthroughs/patient-merge/` (also committed under `docs/testing/patient-merge-walkthrough/`). Test data is synthetic.

| Step | Shows | Screenshot |
|---|---|---|
| 1 | Possible duplicates queue, a pair with its reasons | `01-possible-duplicates.png` |
| 1b/1c | "Not a duplicate" asks for a reason; the pair leaves the list and is listed as dismissed with who and why; it can be suggested again | `01b-dismiss-reason.png`, `01c-dismissed.png` |
| 2 | Side-by-side review, source and survivor explicit, one control per conflicting field | `02-review-side-by-side.png` |
| 3 | Every answer given, reason written, merge confirmed (Merge enabled only now) | `03-review-answered-and-confirmed.png` |
| 4 | Merged: what moved, where the record carries on | `04-merged.png` |
| 4b/4c | The retired record's page: banner, read-only, link to the survivor; the survivor's page shows the lineage | `04b-retired-record-banner.png`, `04c-surviving-record-lineage.png` |
| 5 | Merge log with who and when | `05-merge-log.png` |
| 6 | Undo review: what goes back and what stops it, Undo off until a reason is written and confirmed | `06-undo-review.png` |
| 7 | Undone: both records active again, the log says who and when | `07-undone.png` |

## What the walkthrough found (fixed before this package)
1. A status line made the first `role=status` ambiguous (test selector) - filtered by text.
2. A link inside the red notice failed colour contrast - fixed in `merge.css`.
3. The retired record still offered Save and Reactivate - the details panel is now read-only for a merged-away record, the detail exposes `mergedAtUtc`, and a write answers a clear 409 `patient_merged`.
Each is covered by a test now (`MergeHistoryBanner.test.tsx`, `PatientWorkspace.test.tsx`, `RetiredRecordGuardTests`).
