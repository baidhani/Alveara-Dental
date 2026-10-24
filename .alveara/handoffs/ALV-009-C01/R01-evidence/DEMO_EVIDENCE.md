# ALV-009-C01 R01 - Demo evidence

**Result: passed.** Real Chromium against the real Vite proxy, the real `Alveara.Api` and a fresh SQL Server database, with the real background passes (every 3 s in the walkthrough, 300 s by default). Spec: `src/alveara-client/e2e/work-queue-real-backend.spec.ts`; run with `scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.work-queue.config.ts -OutputVariable WORK_QUEUE_E2E_OUT -OutputDir <dir>`. **8 passed** (`artifacts/walkthroughs/playwright.work-queue.config.ts.txt`, screenshots and JSON in `artifacts/walkthroughs/work-queue-out/`).

1. SETUP: a practice, four staff, a patient whose last completed visit was a year ago.
2. OFF BY DEFAULT: the settings page says automatic recall is off; after three background passes there are no recall tasks and the queue says "Nothing in this view" (`01-recall-off.png`, `02-queue-empty.png`).
3. PERMISSION: billing is refused the queue and the settings (403); a dentist may read but not change the settings (403 on write).
4. TURN ON: the office manager's invalid interval is refused beside the field before anything is sent; after the valid save, exactly one unassigned system-made recall task appears and stays one over further passes (`03-recall-on.png`); axe in light and dark.
5. WORK QUEUE: the dentist sees the task as Unassigned and Overdue (said in words), takes it, raises it to High, completes it with a note (`04-queue-recall.png`, `05-owner-priority.png`); axe in both themes.
6. STALE: a completion from an out-of-date screen is explained ("Someone else changed this follow-up") and stores nothing (`06-stale.png`).
7. TIMELINE: the patient's follow-ups page lists "Made by the system", the assignment and the priority change with who did them (`07-timeline.png`); axe in both themes.
8. AUDIT: a role without follow-up access is refused the timeline; the audit trail holds the follow-up created, assigned, priority-changed and completed events and the recall-settings change.

**Disclosed limits of the demo:** the patient's year-old completed visit is seeded in SQL (a booked appointment marked Completed and moved back a year; no API completes a visit in the past); accepted-but-unscheduled treatment is not in the browser demo (it needs diagnoses, procedures and fees first) and is proven by `UnscheduledTreatmentJobTests` against a real SQL Server; the API process is not killed in the demo (restart safety is proven by the runner-recovery tests).
