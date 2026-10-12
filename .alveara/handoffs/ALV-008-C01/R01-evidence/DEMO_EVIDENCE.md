# ALV-008-C01 R01 - Demo evidence

The visible workflow, run un-mocked: a real Chromium against the real Vite proxy, the real API and a fresh SQL Server database (`e2e/completion-real-backend.spec.ts`, `playwright.completion.config.ts`, 11 of 11; how to run it is in `docs/testing/REAL_BACKEND_E2E.md`). The people are a dentist, a hygienist and a front-desk user, with an administrator creating the catalog and the two providers and acting as the second screen. Screenshots are in `artifacts/walkthrough/` (taken on the final tree by the full sweep).

| Step (test) | What it shows | Screenshot |
|---|---|---|
| SETUP | Ann has a diagnosis linked to a Planned caries finding on tooth 16 O, a diagnosis linked to two findings on tooth 17, a catalog (filling 140, exam 0, scaling 70, crown 100, sealant 40 and others) and five accepted plans | - |
| REVIEW | "Review and complete..." is the primary action; the old button sits in a group labelled "Legacy completion" that says it records no provider, encounter, chart change or charge. The review lists FILL-1 (tooth 16, O: "The chart finding will be marked Completed.", "Charge $140.00"), EXAM-1 ("No charge (no fee)") and SCALE-1 ("Charge $70.00") and the summary "3 procedures will be completed; 2 charges totalling $210.00 will be made; 1 has no fee; 1 chart finding will be marked Completed." Axe in both themes. Looking recorded nothing | `01-review-what-will-happen.png` |
| FAILURE PATH | With the audit write made to fail in the database: "Nothing was completed: The completion could not be saved, so nothing was completed. ..." with no audit or SQL wording, the entered provider kept and no result shown; read back through the API: no completed procedure, no ledger entry, the finding still Planned, every procedure NotStarted. Axe in both themes. After the trigger is dropped the same Confirm click completes all three exactly once | `02-save-failed-nothing-completed.png` |
| RESULT | The result panel: "3 procedures completed", "charged $140.00 (account entry 1)", "charged $70.00 (account entry 2)", "chart finding marked Completed", "no charge"; the ledger holds exactly those two charges; the finding is Completed; the clinical history lists the three with "Performed by ...", the charge and the chart effect (axe both themes); the ledger link opens the billing tab with the two charges | `03-result-recorded.png`, `04-ledger-shows-the-charges.png` |
| a retry or a second submission | The API refuses the same procedures under a new key (`items_not_completable`, three `already_completed`); nothing is duplicated | - |
| AMBIGUITY | CROWN-1 shows "2 chart findings match; unselect this procedure or correct the diagnosis links.", its box is disabled and unticked; SEAL-1 completes alone; the two findings on tooth 17 stay Planned; a forced API call is refused with the matches listed. Axe in both themes | `05-ambiguous-chart-match.png` |
| STALE SECOND SCREEN | The administrator's open review is out of date after the dentist renames the plan: "Nothing was completed: this plan changed since you opened this screen."; after "Reload the review" the same completion works | `06-stale-screen-conflict.png` |
| CORRECTION | An amendment moves the current provider to the second provider and keeps the original; the same key replays; a stale correction is 409; a time before the plan is refused with the reason; the ledger is untouched; the history shows the new provider | `07-correction-shown-in-history.png` |
| the charge is the plan's fee | A request with an `amount` of 1 still charges the plan's 55 | - |
| LEGACY | The old button still records only a state: no completed-procedure record, no charge, and the history entry has no "Performed by" | `08-legacy-completion-leaves-the-detail-empty.png` |
| roles | A hygienist reads the history ("Performed by ...") and has no completion control; the API refuses completing, the readiness and amending (403); front desk reads neither the completed procedures nor the history (403); a write without a CSRF token is 400 | - |

## Truthful states shown
- Loading, empty and failed states: the unit tests show the review's loading text, the failure to load what completing would do (nothing can be completed, with a retry) and the history's empty and failed states.
- Permission states: a reader sees no completion control; the API gives 403.
- Disconnected states: a dropped connection says "Nothing was completed: the connection dropped..." and a retry of the same request reuses its save key (unit tests).
- Nothing is shown as completed unless the server confirmed the whole set.

The viewed screenshots (01 and 05) match what the tests assert. The sticky status bar overlays the lower part of the page in full-page captures; this is existing shell behaviour, not part of this story.
