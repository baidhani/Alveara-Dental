# STORY-016 negative controls (prescriptions)

**Purpose:** prove the new guards are held by tests, not just present. Each control breaks one guard on purpose and runs the story's own tests; a control is **killed** when at least one test fails. Risk declaration: proportionate (new guards on a patient-safety, audit and permission surface; no shared dependency changed beyond appending two permissions).

**Commands.** Backend: `dotnet test src/Alveara.Api.Tests --filter "FullyQualifiedName~Prescription|FullyQualifiedName~AllergyCheck"` (184 tests; clean baseline 184 of 184). Frontend: `npx vitest run src/pages/prescription src/App.patientWorkspaceRoutes.test.tsx` (37 tests; clean baseline 37 of 37). Each mutant was applied to a single place, the tests were run, and the file was restored.

| # | The guard that was broken | Result |
|---|---|---|
| B01 | the typing-error ceiling on a dose never applies | killed |
| B02 | an allergy list that was never reviewed counts as established | killed |
| B03 | the prescriber's own words are never required after an allergy match | killed |
| B04 | an unreadable safety context counts as "no allergies" (fails open) | killed |
| B05 | the audit entry is never written | killed |
| B06 | a hygienist is given the prescribing permission | killed |
| B07 | the append-only trigger does nothing | killed |
| B08 | a reused key with a different entry is not detected | killed |
| B09 | saving needs no CSRF token | killed |
| B10 | the replay lookup is skipped (a replay would be re-checked) | **survived at first**; a test was added (`A_replay_answers_with_the_saved_prescription_without_checking_allergies_again`: the safety context is made unreadable after the save and the replay must still answer); killed on the re-run, and the test passes on the unmutated code |
| F01 | a dose that is not a number is sent to the server | killed |
| F02 | an unreadable safety context is drawn as "no allergies" | killed |

12 controls; 11 killed on the first run, the survivor (B10) exposed a real gap (a replay after the safety context became unreadable, or after an allergy was added, would have been refused or demanded a review note) and is now killed.

**Method note.** Restoring a mutated file leaves an older timestamp, so an incremental build can keep the mutant's binary. After the controls the sources were touched and rebuilt before the full regression, and the new replay test was re-run on the clean build before the controls were recorded.
