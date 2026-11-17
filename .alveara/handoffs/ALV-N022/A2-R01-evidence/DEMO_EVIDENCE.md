# ALV-N022 A2-R01: demo evidence

`finalized-encounter-real-backend` on `6348276` (fresh build, fresh database; set up through the API: a provider, a patient, a Finalized encounter documented with "none reported" and finalized, a Draft encounter, a diagnosis and an accepted one-procedure plan): 1/1, 30 s.

1. Diagnoses tab: the form's encounter choice offers only the Draft encounter; "Finalized encounters cannot receive new diagnoses. Start a new encounter." is shown (`artifacts/walkthrough/01-diagnosis-picker.png`). A direct API create on the Finalized encounter: 409 `encounter_finalized`, the diagnosis count unchanged.
2. Treatment plan tab, "Review and complete": the encounter choice lists both encounters, labelled Draft and Finalized (`02-completion-picker.png`); completing with the Finalized encounter: "1 procedure completed"; the completed procedure carries the Finalized encounter; that encounter's status, row version, finalize time and history length are identical before and after.

Evidence JSON: `artifacts/walkthrough/finalized-encounter-e2e.json`.
