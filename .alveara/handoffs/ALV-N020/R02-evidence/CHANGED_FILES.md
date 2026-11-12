# ALV-N020 R02: changed files

From R01's implementation `17a4775` to R02's `e28282f67e70478529d2c6774e7c83693578a387`, code only (`git diff --name-status 17a4775..e28282f -- src scripts`):

```
M	scripts/Invoke-AccumulatedChain.ps1
M	src/Alveara.Api.Tests/StaffInactivationNotificationSchemaTests.cs
M	src/alveara-client/chain/stage-1.spec.ts
M	src/alveara-client/src/contexts/AuthContext.tsx
M	src/alveara-client/src/contexts/PatientContext.tsx
A	src/alveara-client/src/contexts/authContextObject.ts

 scripts/Invoke-AccumulatedChain.ps1                               | 70 +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++---
 src/Alveara.Api.Tests/StaffInactivationNotificationSchemaTests.cs | 21 +++++++++++++++++++++
 src/alveara-client/chain/stage-1.spec.ts                          | 53 ++++++++++++++++++++++++++++++++++++++++++++---------
 src/alveara-client/src/contexts/AuthContext.tsx                   | 43 +++++--------------------------------------
 src/alveara-client/src/contexts/PatientContext.tsx                |  2 +-
 src/alveara-client/src/contexts/authContextObject.ts              | 40 ++++++++++++++++++++++++++++++++++++++++
 6 files changed, 178 insertions(+), 51 deletions(-)
```

Records in the same range (not code): the R01 handoff and evidence, `.alveara/reviews/ALV-N020/R01.md`, EXECUTION_STATUS.json, BUILD_STATE.md, PROGRAM_REGRESSION_LEDGER.md, PROGRESS.md.

Full story diff from the base `c667037`: R01 `CHANGED_FILES.md` plus the six files above.
