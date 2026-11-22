# ALV-N026 R02: changed files

R02 changes the code only in `fa9a9d5` (on top of the R01 implementation `baaeb51` and the R01 handoff, evidence and stored-review commits `3f6bf99`, `513d483`, `92231f6`): 10 files. `git show --stat fa9a9d5`:

```
 PROGRESS.md                                        |  6 ++
 docs/DOCUMENTS.md                                  |  2 +-
 src/Alveara.Api.Tests/FormsApiTests.cs             | 33 ++++++++
 .../PrescriptionWorkflowApiTests.cs                | 18 ++++
 .../Architecture/Documents/DocumentAccess.cs       |  7 ++
 .../Documents/DocumentLibraryService.cs            |  7 +-
 src/Alveara.Api/Controllers/DocumentsController.cs |  2 +-
 .../e2e/documents-library-real-backend.spec.ts     | 78 ++++++++++++++++-
 .../e2e/documents-real-backend.spec.ts             | 61 ++++++++++----
 .../e2e/prescription-workflow-real-backend.spec.ts | 98 ++++++++++++++++------
 10 files changed, 266 insertions(+), 46 deletions(-)
```

The whole story's changes from the base `318bf3c` are in the package as `diff/implementation.diff` (318bf3c..fa9a9d5); R01's list is `.alveara/handoffs/ALV-N026/R01-evidence/CHANGED_FILES.md`.
