# PROGRESS

- [x] STORY-011: Track patient flow states from scheduled to completed
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: patient flow (Scheduled, CheckedIn, InTreatment, Completed) on the appointment with check-in / start-treatment / complete endpoints, every move logged (history + PHI-free audit) in the same save, and the calendar drawer buttons
  - Verification: 767/767 backend (`dotnet test`, 46 new against real SQL Server), 446/446 frontend (`vitest`, 12 new), `tsc -b` and production build pass, 10/10 real-backend/real-browser walkthrough (`e2e/patient-flow-real-backend.spec.ts`, axe clean in light and dark), 74 mocked browser tests; ALV-004-C01 calendar walkthrough 12/12 and STORY-004 walkthrough 10/10 re-run unchanged
  - Notes: flow is separate from booking status so a visit under way still holds its time; no too-early-to-check-in rule; only ManageAppointments roles can move a patient; no undo. PROGRESS.md was empty before this entry (earlier stories were not logged here).

- [x] ALV-011-C01: Live patient flow board and complete visit-state workflow (companion to STORY-011; set to AWAITING_REVIEW, not COMPLETE)
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: the full visit chain (confirm, check in, ready, seat, treatment, check out, completed) with one patient per room, visit-time provider/operatory, required-form check-in readiness, new front-office/chairside permissions, and the live visit board at /flow
  - Verification: 957/957 backend (`dotnet test`, 190 new against real SQL Server), 484/484 frontend (`vitest`, 38 new), `tsc -b` and production build pass, 74/74 mocked browser, 15/15 real-backend/real-browser walkthrough (axe clean in light and dark); STORY-011 (10/10), ALV-004-C01 (12/12), STORY-004 (10/10), ALV-N010 (10/10), workspace (14/14), STORY-003 (7/7), Gate A route scans (3/3) and repository checks (7/7) unchanged; auth walkthrough 12/12 on the 3rd and 4th runs (a timing-sensitive recovery-key step failed on the first two, ALV-N004's test, untouched)
  - Notes: STORY-011's seven files are byte-identical to the verified commit; Gate B is not evaluated (F1/F2 contrast findings still open); see .alveara/handoffs/ALV-011-C01/R01.md
