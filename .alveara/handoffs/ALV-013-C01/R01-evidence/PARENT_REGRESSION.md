# Parent regression - ALV-013-C01 R01

**Parent course story: `STORY-013`** (portal-verified 2026-10-06T04:46:10Z at `fd7e152`, 3 of 3 criteria, 130 points; completion recorded at `12c8f1a`). Its completion contract is immutable. This attempt extended its model, service, views, controller, client and screen compatibly; its Done Means were run **again after that extension, with its test files unedited**.

| Parent Done Means | Proof on this tree |
|---|---|
| Given a patient encounter, when a diagnosis is recorded, then it is linked to the patient and treatment plan | `DiagnosisServiceTests` (a diagnosis saved linked to the patient and the encounter, with the treatment-plan reference normalized and shown as unresolved) and `DiagnosisApiTests`, `Diagnoses.test.tsx`; the unchanged walkthrough step `ACCEPTANCE 1` (12 of 12). The plan link stays a forward reference: never looked up, always Unresolved |
| Given a diagnosis entry error, when saved, then the system prompts for correction | `DiagnosisRulesTests` (51), `DiagnosisServiceTests` ("Incorrect data is refused naming every problem and nothing at all is saved"), `DiagnosisApiTests`, `Diagnoses.test.tsx`, `diagnosisRules.test.ts`; the unchanged walkthrough step `ACCEPTANCE 2` |
| Trust: all diagnosis entries are logged with user ID and timestamp | `DiagnosisServiceTests` (every record, correction and withdrawal in the audit log with the user and a timestamp; a failed log write saves nothing), `DiagnosisApiTests`; the unchanged walkthrough step `TRUST`; the new walkthrough step `TRUST` extends it to amend, resolve, reactivate and link |

Run results on this tree:
- Backend: all STORY-013 classes pass inside the full run of 2,265. The standalone filter run of every `Diagnosis*` class (256 tests, before the final sweep) also passed.
- Frontend: `Diagnoses.test.tsx` 19, `DiagnosisApi.test.ts` 17, `diagnosisRules.test.ts` 46, all passing, files unedited.
- Real browser: **the original STORY-013 walkthrough, unchanged, 12 of 12** (`artifacts/parent-regression-walkthrough/run-output.txt` from the final sweep; `standalone-run-output.txt` from a separate run).

## What this attempt changed that a parent test could see
- The diagnosis responses gained appended optional properties (`codingSystem`, `code`, `source`, `sourceNote`, `regionKey`, `links`); every old field is unchanged and a diagnosis saved before this attempt reads as `Manual`, uncoded, with no region and no links.
- `DiagnosisInput`, `NormalizedDiagnosis`, `DiagnosisCorrection`, `RecordDiagnosisRequest` and `CorrectDiagnosisRequest` gained trailing optional members; every existing call compiles and behaves as before.
- The status list widened to Active, Resolved, Withdrawn and the change types to include Amended, Resolved and Reactivated. **The default list now shows Active and Resolved** (before, only Active); no Resolved diagnosis could exist before this attempt, so no earlier behaviour changes.
- The history table gained one column, added at the **end** so the existing column positions that STORY-013's screen tests read are unchanged.
- The record form gained optional boxes; the correction form is unchanged except that correcting the tooth of a diagnosis that has a region is refused (amend it instead).
- A visible label inside the record form was renamed during this attempt ("Where it came from") because an earlier wording made Playwright's label match for "Diagnosis" ambiguous in STORY-013's own walkthrough; the walkthrough passes unchanged.
- The migration `AddDiagnosisStructure` was tested up, down and up again with a STORY-013 diagnosis in place; the diagnosis and its reference survive both directions.
- Parent test code touched: none.
