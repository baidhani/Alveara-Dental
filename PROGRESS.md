# PROGRESS

- [x] STORY-011: Track patient flow states from scheduled to completed
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: patient flow (Scheduled, CheckedIn, InTreatment, Completed) on the appointment with check-in / start-treatment / complete endpoints, every move logged (history + PHI-free audit) in the same save, and the calendar drawer buttons
  - Verification: 767/767 backend (`dotnet test`, 46 new against real SQL Server), 446/446 frontend (`vitest`, 12 new), `tsc -b` and production build pass, 10/10 real-backend/real-browser walkthrough (`e2e/patient-flow-real-backend.spec.ts`, axe clean in light and dark), 74 mocked browser tests; ALV-004-C01 calendar walkthrough 12/12 and STORY-004 walkthrough 10/10 re-run unchanged
  - Notes: flow is separate from booking status so a visit under way still holds its time; no too-early-to-check-in rule; only ManageAppointments roles can move a patient; no undo. PROGRESS.md was empty before this entry (earlier stories were not logged here).
