# ALV-009-C02 R01 - Demo evidence

**Result: passed.** Real Chromium against the real Vite proxy, the real `Alveara.Api` and a fresh SQL Server database, with the real background pass (every 3 s in the walkthrough, 300 s by default). Spec: `src/alveara-client/e2e/external-cases-real-backend.spec.ts`; run with `scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.external-cases.config.ts -OutputVariable EXTERNAL_CASES_E2E_OUT -OutputDir <dir>`. **9 passed** (`artifacts/walkthroughs/playwright.external-cases.config.ts.txt`; screenshots and JSON in `artifacts/walkthroughs/external-cases-out/`).

1. SETUP: a patient with a radiograph already in the document library.
2. PARTIES: the office manager's empty form is refused beside the field; a referral provider and a lab vendor are added (`01-parties.png`); a dentist is refused (403); axe in light and dark.
3. REFERRAL: the dentist's empty referral form is checked first (no case stored); the referral is saved with a follow-up date of yesterday (`02-new-referral.png`) and, being a draft, makes no chase; it is sent with a date four days back, is shown OVERDUE in words, and the radiograph is linked from the library (`03-referral-detail.png`): the library still holds one document.
4. CHASE: within seconds the system makes exactly one unassigned High-priority task, still one after more passes; the front desk finds it in the work queue with Unassigned and Overdue (`04-queue-chase.png`), axe in both themes, and follows "Open the case".
5. COMPLETE: the report is recorded, the case completed with a note, no Edit button remains (`05-referral-completed.png`), and the system closes the chase task by itself (closed by the system); history reads Draft to Sent, Received to Completed with who did each; axe in both themes.
6. LAB: a lab case with tooth, material and shade; "3x" is refused as a tooth on the form; it goes out and comes back; sending it back for a remake needs a note (checked before anything is sent); someone else's edit makes the attempt stale and it is explained, storing nothing (`06-lab-stale.png`); after the reload the same move succeeds and the received date is cleared.
7. BROKEN LINK: a document whose file is marked missing stays in the case with the reason (`07-broken-link.png`).
8. ROLES: a hygienist reads the list and the patient tab with no New buttons and no actions; billing is refused (403) on the list and the timeline; a hygienist cannot create a referral (403); axe in both themes.
9. AUDIT: the audit trail holds the party, referral, lab and document-link events and none of the reasons or notes.

**Disclosed limits of the demo:** the document is inserted into the library by SQL (the walkthrough is about linking, not importing); the API process is not killed in the demo (restart safety is proven by the runner-recovery tests); the case is made overdue by sending it with a past date, not by waiting for a calendar date to pass.
