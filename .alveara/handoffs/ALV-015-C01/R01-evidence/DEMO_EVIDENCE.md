# ALV-015-C01 R01 - Demo evidence

The demo is `e2e/treatment-plans-complete-real-backend.spec.ts`: a real Chromium against the real Vite proxy, the real `Alveara.Api` and real SQL Server LocalDB, on a fresh database. It passed 10 of 10 (27.8 s in the sweep, `artifacts/walkthrough/sweep-run-output.txt`; screenshots and `treatment-plans-complete-evidence.json` are in `artifacts/walkthrough/`). To run it again see `docs/testing/REAL_BACKEND_E2E.md`.

| Step (a test of the spec) | What is shown | Screenshot |
|---|---|---|
| SETUP | two patients; diagnoses, two of them with old references ("plan-7", "old note") and one whose text ("SHARED") is another patient's plan code; three catalog procedures | - |
| A plan is created, a second procedure added on screen, a third through the API | the STORY-015 flow, $220.00 estimate | - |
| REVISION | saving without a reason refused beside the field, nothing saved; then one revision with a rationale, a patient explanation and two phases; the plan grouped by phase with the unplaced procedure last; the money unchanged | 01-revise-form, 02-grouped-by-phase |
| ANSWERS | a decline refused without a reason; an acceptance with who gave the answer and who entered it; the whole-plan answer refused ("1 of the 3 procedures already declined"), nothing recorded; "Partly accepted"; accepted-so-far $150.00 | 03-partial-conflict, 04-answers |
| PROGRESS | a declined procedure offers no scheduling; an accepted one is scheduled with a date ("It does not book an appointment") and completed after a confirmation; no control is left on it; re-answering, rescheduling and withdrawing it are refused (409 `item_completed`) | 05-completed |
| HISTORY | four revisions listed; revision 1 opened as saved (one procedure, no phases); revisions 3 and 4 compared: rationale added, two phases added, procedures moved, the estimate unchanged | 06-compare |
| PATIENT COPY | the plain-language page, with no staff-only text, the estimate and its caveats; under print media everything else is hidden and the page is black on white | 07-patient-copy, 08-patient-copy-print |
| OLD REFERENCES | a reference code set; review changes nothing; "plan-7" tied by its code; "old note" tied by choice from a list offering only this patient's plans; "SHARED" stays unresolved; the Diagnoses tab shows both ties with the original text and the diagnosis history untouched | 09-references-review, 10-references-tied, 11-diagnoses-resolved |
| A hygienist | reads the plan, its answers, its history, the comparison and the patient copy; no control; five write attempts through the API all return 403 | 12-hygienist-read-only |
| The API | a stale answer 409; another patient's plan 404 (the same words as an unknown plan); a missing CSRF token 400; a resolved reference cannot be changed (409) while the diagnosis's label still can; the audit log holds all seven new event types | - |

Axe scans in light and dark (0 critical or serious on each): revise form, grouped plan, history and comparison, patient copy, references panel, Diagnoses screen with a tied reference, read-only view (`artifacts/walkthrough/treatment-plans-complete-evidence.json`, 14 results).

## Honest notes
- Screenshots are full-page captures: a fixed-position toast ("Plan revised: ...") sits over one row in `02-grouped-by-phase.png`; it is the notification, not a layout defect.
- The print layout is shown by a screenshot under print media and by assertions (the rest of the page hidden, black on white); it was not printed on paper.
- The first two standalone runs of this spec failed (a mistake in the spec's own selector, then a real contrast defect in the dark theme found by the axe scan, fixed); the third standalone run and the sweep run were clean.
