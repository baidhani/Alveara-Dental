# Parent regression — STORY-005 (immutable course contract) — ALV-005-C01 R01

Parent `STORY-005`: portal-verified at `d87ceae` (sync commit `cba1e2f`, 3 of 3 criteria). Its test files were not edited by this attempt (`git diff` of the implementation commit shows no change to them).

| STORY-005 Done Means | Still proven by (run on the final code) |
|---|---|
| Given a patient encounter, when documented, then the system records medical and dental history | `EncounterServiceTests.A_documented_encounter_records_medical_and_dental_history_allergies_and_medications_with_who_and_when` and the rest of the 37; `EncountersApiTests` 25; `ClinicalEncounterSchemaTests` 34 — all pass in the 1,232 run. Browser: `clinical-real-backend.spec.ts` ACCEPTANCE 1 and INCOMPLETE DOCUMENTATION. |
| Given an encounter note is finalized, when an amendment is needed, then the original is preserved with an addendum | `EncounterServiceTests` addendum tests (including audit failure and replay); browser ACCEPTANCE 2 and DATA LOSS (unchanged spec). The addendum request gained an **optional** `section`; omitted, behaviour and responses are as before (the three-argument `AddAddendumAsync` remains). |
| Trust: all documentation changes are logged with user and timestamp | `EncounterServiceTests` audit tests; browser TRUST (unchanged spec). New audit events follow the same rules. |

## Before and after
- **Before:** STORY-005's recorded verification (backend 1,071 of 1,072 with the one failure corrected, frontend 599, real-backend walkthrough 9 of 9).
- **After (this attempt):** backend 1,232 of 1,232 (all of the above included), frontend 684 of 684, `clinical-real-backend.spec.ts` **9 of 9** against the new build (`artifacts/regression/story-005-walkthrough-run-output.txt`).

## How the parent contract was protected
- Everything added to the parent's records is additive: new tables, new nullable columns (`Encounters.SignedAtUtc/SignedByUserId/TemplateId/TemplateName`, `EncounterAddenda.Section`), new optional fields on the encounter view.
- `IsComplete` and `MissingSections` keep their STORY-005 meaning; the new `ReadyToSign`/`MissingNotes` are separate. Finalizing an encounter with no template applied behaves as before (`Without_a_template_finalizing_works_exactly_as_it_did_in_story_005`).
- The parent's error text and codes are unchanged (`documentation_incomplete` message for missing sections is identical when no required note is missing; "Review before finalizing" region, "Review and finalize" and "Finalize encounter" controls, "Added ..." addendum lines are preserved).
- The parent's database triggers (51030-51037) and check constraints were not changed; the new migration only adds columns and tables.

## Dependency regression
`ALV-011-C01` (permission ordering test remains relative), `ALV-002-C01` (shared conflict banner/concurrency guard used unmodified) — covered by the full backend run and the mocked/real-backend runs listed in `TEST_RESULTS.md`; workspace 14 of 14 and forms 10 of 10 real-backend walkthroughs pass on the new build.
