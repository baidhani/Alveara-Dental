# ALV-N022 A3-R01: acceptance evidence

Against Addendum 02 section 6.5 (the Stage 1 matrix approved by review A3-STEP0-ADDENDUM-02). Server tests in `artifacts/backend-focused-tests.csv`; client tests in the focused vitest; browser in `artifacts/walkthroughs/`.

| Item | Server proof | Client / browser proof | Status |
|---|---|---|---|
| F-106 tab shows status, instructions, quantity, supersession; a pre-workflow row says so | `PrescriptionWorkflowServiceTests` (list carries the workflow fields and supersession) | `PrescriptionList.test.tsx` (2); a3-display test 1 (`01-prescriptions-tab.png`) | PROVEN |
| F-105 no verdict word on any no-match surface | `tests/allergy-wording-guard.test.mjs` (3 surfaces) | control NC1 caught | PROVEN |
| F-199 "has"/"have" per label | `SafetyContextServiceTests` ("Medical history has not been reviewed...") | - | PROVEN |
| F-090 (D-1 B) clearance document: same-patient, active, no free text (service) | `ClearanceServiceTests` (another patient's / unknown / inactive document refused, nothing changes; free text refused) | safety walkthrough (API: free text 400 `documentReference`, another patient's 400 `documentId`) | PROVEN |
| F-090 direct-insert refusal 51288; legacy text kept; migration up/down; refused down with ids present | `TypedLinkGuardMigrationTests` (4) | control NC2: service check removed, refusal still held by the trigger | PROVEN |
| F-090 picker lists only the patient's own active documents; legacy shown read-only | - | `Safety.test.tsx` (inactive not offered; attach by picker); safety walkthrough (another patient's document not offered) | PROVEN |
| F-098 (D-1 B) perio diagnosis: same patient, not withdrawn, no free text (service) | `PerioSessionServiceTests`, `PerioSessionApiTests` | a3-display test 3 (API: another patient's diagnosis 400, free text 400) | PROVEN |
| F-098 direct-insert refusal 51289; a DiagnosisId needs a Diagnosis link | `TypedLinkGuardMigrationTests` | - | PROVEN |
| F-098 picker lists only the patient's diagnoses; legacy labelled | - | `pages/A3Display.test.tsx` (chart's patient only, withdrawn not offered, id sent, legacy labelled; added in `63f74ff`); a3-display test 3; perio-sessions walkthrough | PROVEN |
| Q-5aa card read lists linked exams | `PerioSessionServiceTests` (`LinkedFromPerioCharts`) | `pages/A3Display.test.tsx` (card shows them; added in `63f74ff`) | PROVEN |
| F-091 (CR-1) mark only for Existing/Completed missing; Diagnosed/Planned show status only; implant + missing both listed | - | `OdontogramA3.test.tsx` (rule, tile, implant + missing); a3-display test 2 (`02-absent-mark.png`); control NC3 caught | PROVEN |
| F-093 tooth change clears the message | - | `OdontogramA3.test.tsx` | PROVEN |
| F-094 identical repeat says nothing changed | - | `OdontogramA3.test.tsx` | PROVEN |
| F-095 Planned/Completed explained | - | `OdontogramA3.test.tsx` | PROVEN |
| F-097 visible Depth / Rec. labels | - | `pages/A3Display.test.tsx` (192 sites) | PROVEN |
| F-099 one numbering per screen | - | `pages/A3Display.test.tsx` (diagnosis history, clinical history); `TreatmentPlanClinicalHistory.test.tsx`, `DiagnosisStructure.test.tsx`; diagnoses-structure walkthrough | PROVEN |
| F-067 sex shown in one form (stored value unchanged) | - | `A3Slice4.test.tsx` (rule, header) | PROVEN |
| F-082 the count says what it counts | - | `A3Slice4.test.tsx`, `Clinical.test.tsx` | PROVEN |
| F-137 the client checks the tooth | - | `A3Slice4.test.tsx`, `ExternalCaseScreens.test.tsx`; external-cases walkthrough | PROVEN |
| Q-5ai tooth pre-filled from the diagnosis, only when empty | - | `A3Slice4.test.tsx` (2) | PROVEN |
| Q-5j practice-level route; old URL redirects | - | `A3Slice4.test.tsx` (path rules incl. an invalid `from`, redirect with the way back, direct visit); clinical-companion walkthrough follows the link | PROVEN |
| D-3 sentence | - | `CompletionResultPanel.test.tsx` | PROVEN |
| CR-2 one fresh-database real-browser spec, three workflows | - | `a3-display-real-backend.spec.ts` 3/3 | PROVEN |
| Negative controls (three) | NC2 | NC1, NC3 | AS DESIGNED |
| No external medication service, allergy-class, interaction, dose or compatibility logic | nothing added | - | AS APPROVED |
