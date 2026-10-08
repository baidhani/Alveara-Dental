# ALV-N005 R01 - Parent regression

**Not applicable as a parent comparison.** `ALV-N005` is a new-production story with no course parent (`parentCourseStory: null`); there is no parent "Done Means" to map.

Because the story adds a migration to a shared database and a route and navigation entry to the shared shell, the complete evidence sweep was run anyway, so that nothing already shipped was disturbed. Each real-backend walkthrough ran on its own freshly created and migrated database against the real API (`artifacts/regression/`; sweep 2026-10-08, 13:57:47Z to 14:08:12Z, 10 m 25 s):

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
| procedures (new, this story) | 6 passed |

Also on the same tree: mocked browser suite 136 of 136 (it includes the shell navigation and accessibility specs that the new nav entry could affect), frontend 1214 of 1214, repository checks 8 of 8, backend full serial 2386 of 2386.

The only existing files touched by this attempt are `AlveraDbContext.cs` (three `DbSet`s and one `Configure` call), `Program.cs` (two registrations), the model snapshot (additions only), `App.tsx` (one route), `moduleRegistry.ts` (one entry), `playwright.config.ts` (one excluded spec name) and two documentation files. Every other existing behavior is exercised by the runs above.
