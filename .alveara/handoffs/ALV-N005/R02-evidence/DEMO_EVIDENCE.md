# ALV-N005 R02 - Demo evidence

A real Chromium against the real Vite proxy, the real `Alveara.Api` and a real LocalDB database (`e2e/procedures-real-backend.spec.ts` through `playwright.procedures.config.ts`; **7 of 7 passed**, 17.0 s, standalone run on a fresh database). Screenshots and the JSON evidence (`procedures-evidence.json`, including the axe results) are in `artifacts/walkthrough/`; all names are synthetic.

The six R01 steps are unchanged and still pass (see `../R01-evidence/DEMO_EVIDENCE.md` for their description): the empty catalog, an incomplete entry refused beside its fields, a practice code that looks like CDT refused by the server with the typed values kept (`01-wrong-entry-refused.png`), the procedure added at $95.00 (`02-added.png`), the fee change as a new version with a required reason and who and why in the history (`03-fee-history.png`), inactivate and reactivate (`04-reactivated.png`), the dentist's read-only view (`05-dentist-read-only.png`), the hygienist denied (`06-hygienist-denied.png`), and the API refusing what the screen does not offer.

## New in R02 - the corrected provenance rule (test 7)
| Step | What it shows | Evidence |
|---|---|---|
| Billing posts a CDT procedure to the API with no source name | `400 validation_failed` with `fieldErrors.sourceName` beginning "Name the source" (the case the review reproduced) | test 7 |
| Billing opens **Add a procedure**, chooses the CDT code system, enters `D1110`, a description, a category, "Whole mouth" and a fee of 95, and presses Add | "Name the source of this code set." beside the **Source of the code set** field, nothing sent; the edition field is marked optional | `07-cdt-needs-source.png` |
| Billing enters "Licensed code set held by the practice" and the edition "2026" and presses Add | Status "D1110 added to the catalog."; the row reads "CDT code (Licensed code set held by the practice, 2026)", $95.00, Preventive, Whole mouth | `08-cdt-added.png` |
| axe scans, light and dark | 0 critical or serious violations | `procedures-evidence.json` (`cdt-added-light`, `cdt-added-dark`) |

## States covered
Same as R01: empty, refused entry with typed values kept, saved, read-only, permission denied. The loading state and the "connection dropped" message are not asserted by any test (stated in `../R02.md`). The usage warning with a non-zero count is proven by the unit tests (screen) and the backend lifecycle test (service); the browser walkthrough does not create an odontogram finding link.

## One visible wording flaw (disclosed, not changed)
In `07-cdt-needs-source.png` the Code field's hint still reads "It cannot be changed later. A practice code must not look like a CDT code (D followed by four digits)." while the CDT code system is selected. It is accurate for practice codes and not relevant to CDT codes; changing it is outside the review finding.
