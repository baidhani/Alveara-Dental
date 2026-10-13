# ALV-008-C02 R01 - Demo evidence

The walkthrough is `src/alveara-client/e2e/completion-adoption-real-backend.spec.ts`: a real Chromium, the real Vite proxy, the real `Alveara.Api` and a fresh LocalDB database, one signed-in session per role (administrator, dentist, hygienist, front desk). Procedures completed the old way are seeded straight into the database with `e2e/support/legacyCompletion.ts` because the product can no longer make them. 8 of 8 passed, final tree. Screenshots are in `artifacts/walkthrough/` and were viewed before being cited.

| # | Screenshot | What it shows |
|---|---|---|
| 01 | `01-legacy-completions-waiting.png` | Three procedures completed the old way: no "Complete accepted procedures" group, no per-procedure completion form, no "Review and complete" (nothing is left to complete), and "Adopt earlier completions..." offered. The reconciliation (API) reads three waiting |
| 02 | `02-adopt-form-effects-and-gaps.png` | The adopt form: it says what adopting FILL-1 will do ("the linked chart finding will be marked Completed; a charge of $140.00 will be made") and names every missing field beside the field (provider, encounter, why) without sending anything. (The floating "Nothing is saved until you create a plan." banner is the app's existing sticky notice, which a full-page capture places mid-page) |
| 03 | `03-adopted-in-history.png` | After the adoption: the success message, and the clinical history entry for FILL-1 reading "Completed by ... Performed by ... charged $140.00 (account entry 1) - chart finding completed" while the other two still read only "Completed by" |
| 04 | `04-failed-adoption-records-nothing.png` | With the audit log made unwritable the adoption says "Nothing was recorded: The adoption could not be saved...", with no audit or SQL words, keeps what was typed and offers Reload; nothing is stored |
| 05 | `05-stale-second-screen.png` | A second screen opened before the plan was renamed on the first is told "Nothing was recorded: this plan changed since you opened this screen" |
| 05b | `05b-revised-original-kept.png` | After a revision the original revision is kept and the completed care stays in the history (STORY-008 criteria 2 and 3, ported) |
| 06 | `06-gate-d-reconciled.png` | The end state: all three procedures adopted and the history complete; the reconciliation (API) reads all zero |

Also asserted in the same run but not screenshotted: the old routes answer 410 and change nothing; a hygienist has no adopt button and gets 403 from the server, front desk 403, a missing CSRF token 400, a repeat adoption 409 `not_a_legacy_completion`, and only the audit-log roles read the reconciliation; the three charges are $50, $70 and $140 and the finding on tooth 16 is Completed; axe light and dark with the adopt form open found 0 critical or serious violations.
