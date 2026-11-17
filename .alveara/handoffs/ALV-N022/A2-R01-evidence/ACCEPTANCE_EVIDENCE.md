# ALV-N022 A2-R01: acceptance evidence

Against the amended briefing section 10 A2 row and the Stage 1 contract of review A2-STEP0-ADDENDUM-03.

| Requirement | Proof | Status |
|---|---|---|
| Diagnosis create: Draft accepted | DiagnosisServiceTests `A_diagnosis_on_a_draft_encounter_is_recorded`; existing create tests | PROVEN |
| Diagnosis create: Finalized refused before any write (no diagnosis, version, audit; encounter unchanged) | `A_diagnosis_on_a_finalized_encounter_is_refused...`; browser API refusal with the count unchanged; control N1 | PROVEN |
| Another patient's encounter refused | existing `An_encounter_of_another_patient_or_one_that_does_not_exist_is_refused_the_same_way...` | PROVEN |
| Auth | existing DiagnosisApiTests role/anonymous tests (28 passed) | PROVEN (unchanged routes) |
| Completion: Draft and Finalized attribution, encounter unchanged | existing `A_draft_or_a_finalized_encounter_can_be_named_and_the_encounter_itself_is_never_changed`; browser (status, version, finalize stamp, history unchanged); control N2 | PROVEN |
| Adoption: Draft and Finalized attribution, encounter unchanged | new adoption theory | PROVEN |
| Amendment: Draft and Finalized re-attribution, encounter unchanged | existing finalized test + new Draft companion | PROVEN |
| Completion/adoption: another patient's encounter refused | existing adoption and completion tests | PROVEN |
| Diagnosis picker excludes finalized encounters, with the message | `DiagnosisFormEncounters.test.tsx`; browser | PROVEN |
| Complete and adopt pickers still list Draft and Finalized with status | adoption picker client test; browser (complete picker) | PROVEN |
| Re-link refused | no re-link path exists (Addendum 02, verified) | NOT APPLICABLE |
| Signed drafts unchanged; no signed-draft test | nothing changed for them | AS APPROVED |
| Documents and referrals unchanged | no change in their code | AS APPROVED |
| Negative controls (both) | N1, N2 caught, restored | PROVEN |
