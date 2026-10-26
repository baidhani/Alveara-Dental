# ALV-017-C01 — Independent Stage 1 Review R02

**Decision:** CHANGES_REQUIRED  
**Reviewed:** 2026-10-26  
**Handoff:** `Alveara_Handoff_ALV-017-C01_f8d40e9_R02.zip`  
**Package SHA-256:** `EEC5289F6B0AA23BA1BF2A2D0D49C8AD2A39F97871B146632187C8E0AF497019`  
**Implementation reviewed:** `f8d40e945f8aab438c80f72128ee309c05b0a8f5`

## Ruling

The R02 technical correction is materially sound, but the submitted package cannot be approved because its canonical `MANIFEST.json` reports stale R01 test results rather than the actual R02 evidence. A manifest is a control artifact, not summary prose; it must accurately identify the evidence it packages.

## Evidence supporting the correction

The repository and package evidence support the intended R02 remediation:

- The new runner preflight validates each walkthrough’s output-variable contract and rejects both the R01 `PATIENTS_E2E_OUT` mismatch and an output folder inside the repository.
- The runner uses `PATIENT_E2E_OUT` for the patients walkthrough and directs all walkthrough output to the supplied external run folder.
- SQL error 3702 is handled as a narrowly classified, bounded drop retry. The diagnostic records only session ID, program name, host, host process ID, and status; focused tests assert that connection strings and statements are not logged.
- Focused tests reproduce a real 3702, prove successful cleanup after a transient holder, prove failure after the bounded four attempts for a persistent holder, and verify host/report drain behavior. The focused final-tree TRX is **1,082/1,082**; the five repeated permission-policy runs also pass.
- The regenerated identity derivation is internally consistent: 5,080 baseline IDs − 1 declared renamed/retired ID + 57 new IDs = the frozen R02 Stage 2 expectation of **5,136**.

## Required correction

`MANIFEST.json` currently states the old R01 test summary: focused backend 3,076/3,076, `*ApiTests` 883/883, 52 new IDs, and an expected Stage 2 total of 5,131. Those values contradict the package’s R02 `TEST_RESULTS.md`, identity derivation, and final-tree TRX.

Create a new Stage 1 package with a corrected canonical manifest that identifies the R02 evidence accurately, including:

- focused final-tree run **1,082/1,082**;
- 57 new IDs and one declared retired ID;
- frozen Stage 2 expectation **5,136 passed, 0 failed, 0 skipped**;
- the relevant targeted repeat, runner-preflight, negative-control, and real-backend evidence.

No source change and no full regression are required merely to correct this control-record error. Preserve this rejected R02 package and submit the corrected material as the next immutable Stage 1 attempt. Do **not** begin a Stage 2 regression until that package receives `APPROVED_PENDING_REGRESSION`.
