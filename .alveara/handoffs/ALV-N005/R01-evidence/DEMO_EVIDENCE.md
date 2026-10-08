# ALV-N005 R01 - Demo evidence

A real Chromium against the real Vite proxy, the real `Alveara.Api` and a real LocalDB database (`e2e/procedures-real-backend.spec.ts`, run through `playwright.procedures.config.ts`; 6 of 6 passed, 13.7 s). Screenshots and the JSON evidence (`procedures-evidence.json`, including the axe results) are in `artifacts/walkthrough/`. All names are synthetic (`billing-<timestamp>`, "Beth Billing <timestamp>").

| Step the walkthrough takes | What it shows | Evidence |
|---|---|---|
| Billing signs in and follows **Procedures & fees** in the navigation | The page's empty state: "No procedures found" | test 1 |
| Billing presses **Add procedure** on an empty form | "A code is required." and "A fee is required (enter 0 for no charge)." beside their fields, nothing sent | test 1 |
| Billing enters `D1110` as a practice code with a description, category, applicability and fee | The server refuses: "That looks like a CDT code. Choose the CDT code system for it, or give the local code a different shape (for example LOCAL-1234)." beside the code field; what was typed is kept; the status line says "Not saved: Some fields need attention." | `01-wrong-entry-refused.png` |
| Billing corrects it to `CLEAN-1` and adds it | Status "CLEAN-1 added to the catalog."; the list shows **$95.00**, Active, practice code, Preventive, Whole mouth | `02-added.png` |
| axe scans of the catalog, light and dark | 0 critical or serious violations in both themes | `procedures-evidence.json` |
| Billing changes the fee to 105.50 without a reason | "Say why." beside the reason box, nothing sent; the code is shown but cannot be edited | test 2 |
| Billing gives the reason "Annual fee review" and saves | "CLEAN-1 changed."; the list shows **$105.50**; opening "Fee and change history" shows **Version 1: $95.00**, **Version 2: $105.50 - Reason: Annual fee review** and the events Created and Revised, each by Beth Billing with the time | `03-fee-history.png` |
| axe scans of the history view, light and dark | 0 critical or serious | `procedures-evidence.json` |
| Billing inactivates without a reason, then with "No longer offered" | "Say why." first; then "CLEAN-1 inactivated."; the row shows **Inactive**; the planning list (`/api/procedures/active`) is empty | test 3 |
| Billing reactivates | "CLEAN-1 reactivated."; the planning list shows `CLEAN-1` again | `04-reactivated.png` |
| A dentist opens the catalog | Reads the list and the current fee ($105.50); **no** Add, Change or Inactivate controls; the status line says "You can read the catalog but your role cannot change it."; axe clean in both themes | `05-dentist-read-only.png` |
| A hygienist | has no **Procedures & fees** link; going to `/procedures` says permission is needed | `06-hygienist-denied.png` |
| The API is called directly | Dentist change: 403; hygienist read: 403; billing change without a CSRF token: 400; a revise from a stale copy: 409; a duplicate code with different content: 409 `procedure_exists`; the audit log holds `ProcedureDefinition` entries | test 6 |

## States covered
Loading (the standard loading state is shown while the list loads; there is no test that asserts it, so it is not claimed as tested), empty (screenshot above), error with a retry (unit test), permission denied (hygienist), read-only (dentist), refused entry with typed values kept (screenshot), saved, conflict (unit test shows the shared banner and the open form kept; the browser test checks the 409), usage-warning dialog (unit tests; the browser walkthrough has no referencing record, so it shows the unreferenced path). Offline/disconnected: the page has a "connection dropped" message that keeps what was typed (the same handling as the odontogram panel), but no test exercises it, so it is not claimed as tested.

## Not shown in the browser
The usage warning with a non-zero count is proven by the unit tests (screen) and by the backend lifecycle test against the real odontogram service (service); the browser walkthrough does not create an odontogram finding link.
