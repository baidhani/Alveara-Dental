# Parent regression — ALV-N011 R01

**Parent course story: none.** ALV-N011 is New Production work with no course parent (the Execution Plan states "No course parent"), so there is no immutable course contract to map. The explicit `N/A` is justified; what this attempt could still break is the behaviour of the stories it depends on and the screens it touches, which was re-run instead.

## Dependency and touched-surface regression (on the final code)
| Dependency / surface | What changed in it | Proven unchanged by |
|---|---|---|
| `ALV-005-C01` clinical record (`ClinicalRecordItem`, section statements, `ClinicalRecordReader.SectionStatus`) | read-only use; `ClinicalRecordReader.SectionStatus` is called, nothing in the record changed | its 160 backend tests and 85 frontend tests pass in the full runs; real-backend companion walkthrough **9 of 9** |
| `STORY-005` encounters | the encounter screen gained a safety strip above the sections; no other change | `Clinical.test.tsx` and its contrast test pass unmodified; real-backend STORY-005 walkthrough **9 of 9** |
| `ALV-011-C01` / `STORY-011` visit board | `VisitCard` gained one optional field `safety`; `VisitBoardService` gained an optional constructor argument and an optional `includeSafety` parameter | all existing board tests unchanged and passing (`VisitBoardTests` 10, `VisitsApiTests` 47, `FlowBoard.test.tsx`); real-backend board walkthrough **15 of 15**, STORY-011 flow walkthrough **10 of 10** |
| `ALV-002-C01` concurrency | `ConcurrencySaveGuard` and the shared conflict banner used unmodified | the new alert/clearance stale-edit tests, UI banner tests and the real-browser conflict test |
| Permission matrix | one permission appended last (`ViewSafetyIndicator`); no existing assignment changed | `ClinicalPermissionTests` (ordinal and "never wider than clinical read"), the permission-matrix tests in the full backend run |
| Patient header (every patient screen) | gains the safety strip for roles that may read clinical documentation | workspace **14/14**, forms **10/10**, patients **7/7**, calendar **12/12**, schedule **10/10**, auth **12/12** real-backend walkthroughs; mocked browser **94/94**; frontend **733/733** |

No file of a dependency's own test suite was edited except `App.patientWorkspaceRoutes.test.tsx`, whose pinned list of workspace tabs gained the new `safety` entry (the tab list is the extension point that test documents).
