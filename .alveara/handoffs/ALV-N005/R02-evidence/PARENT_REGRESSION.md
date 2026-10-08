# ALV-N005 R02 - Parent regression

**Not applicable as a parent comparison.** `ALV-N005` is a new-production story with no course parent (`parentCourseStory: null`); there is no parent "Done Means" to map.

Because this attempt adds a second migration to a shared database, the complete evidence sweep was rerun on the corrected tree. Each real-backend walkthrough ran on its own freshly created and migrated database (both ALV-N005 migrations applied) against the real API (`artifacts/regression/`; sweep 2026-10-08, 15:41:05Z to 15:53:05Z, 12 min 0 s):

| Walkthrough | Result |
|---|---|
| auth | 12 passed |
| patients | 7 passed |
| workspace | 14 passed |
| forms | 10 passed |
| schedule | 10 passed |
| calendar | 12 passed |
| flow | 10 passed |
| board | 15 passed |
| clinical | 9 passed |
| companion | 9 passed |
| safety | 11 passed |
| odontogram | 11 passed |
| odontogram longitudinal | 11 passed |
| perio | 10 passed |
| perio sessions | 12 passed |
| diagnoses | 12 passed |
| diagnoses structure | 12 passed |
| **existing walkthroughs, total** | **187 passed, 0 failed** |
| procedures (this story, 7 tests including the new CDT step) | 7 passed |

Also on the same tree: mocked browser suite 136 of 136, frontend 1215 of 1215, repository checks 8 of 8, backend full serial 2405 of 2405.

The existing files touched by this attempt are `ProcedureRules.cs` (two added lines), `ProcedureModel.cs` (one trigger registration and one check constraint), the model snapshot (additions only), the catalog's own tests and e2e spec, and `docs/PROCEDURE_CATALOG.md`. No other existing behaviour was changed; the runs above exercise all of it.
