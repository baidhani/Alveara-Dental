# ALV-003-C01 R02 — Parent regression (STORY-003)

STORY-003 is unchanged (`COMPLETE`, portal-verified). Its six test files are byte-identical to the parent baseline (`git diff bacf6ed HEAD` on them is empty) and pass inside the R02 runs: backend `PatientModelTests` + `PatientRegistrationServiceTests` + `PatientsApiTests` 45/45 (within 492); frontend `PatientRegistrationPage.test.tsx` + `App.patientRoute.test.tsx` 11/11 (within 319). The real-backend STORY-003 walkthrough (7/7) is R01 evidence on the same tree. Detail: `../R01-evidence/PARENT_REGRESSION.md`.
