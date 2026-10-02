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

- [x] ALV-N001 R04: status text colours meet WCAG AA in both themes (finding F1 and siblings; set to AWAITING_REVIEW, not COMPLETE)
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: new text-only tokens `--color-success-text` / `--color-warning-text`, dark `--color-info` lightened, and the success/warning notifications and the shell session-warning banner repointed; 4.45:1 / 3.47:1 / 4.42:1 / 4.05:1 / 4.39:1 pairings now >= 4.5:1
  - Verification: 498/498 frontend (`vitest`, 14 new in `statusContrast.test.ts`, 9 of them fail against the old values), `tsc -b`, build and lint pass, mocked Playwright 78/78 (4 new real-browser notification axe tests, which fail against the old CSS at 4.45:1 and 3.46:1), real-backend: Gate A route scans 3/3, board 15/15, calendar 12/12, forms 10/10, workspace 14/14, STORY-003 7/7, scheduling 10/10, STORY-011 10/10
  - Notes: colour-only; the ALV-002-C01 banner (F2) and the removal of its three page workarounds are the next separate attempt; the system-status stale banner (ALV-N002) has the same warning-pair defect and is reported, not changed

- [x] ALV-002-C01 R05: conflict-banner title meets WCAG AA and the three page-level workarounds are removed (finding F2; set to AWAITING_REVIEW, not COMPLETE)
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: the shared ConcurrencyConflictBanner title uses `--color-warning-text` (3.46:1 -> 5.39:1 light); the `.cal-drawer`, `.alv-workspace__form/__panel` and `.flow-board` title overrides are deleted; `patientWorkspaceContrast.test.ts` no longer asserts the title fails; `docs/SCHEDULING.md` updated
  - Verification: 502/502 frontend (`vitest`, 5 new in `conflictBannerContrast.test.ts`: 2 fail against the old CSS incl. a guard that no page re-colours the title), `tsc -b`, build and lint pass, mocked Playwright 78/78, real-backend with the overrides gone: Gate A scans 3/3, board 15/15, calendar 12/12, forms 10/10, workspace 14/14, STORY-003 7/7, scheduling 10/10, STORY-011 10/10, auth 12/12; mutation check: reverting only the banner colour fails the board, calendar and workspace walkthroughs on axe color-contrast
  - Notes: colour-only; ALV-N002's `.status-stale-banner` (same warning-pair defect) is the next separate attempt

- [x] ALV-N002 R09: System Status stale-data banner meets WCAG AA (set to AWAITING_REVIEW, not COMPLETE)
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: `.status-stale-banner` text uses `--color-warning-text` (3.46:1 -> 5.39:1 light); new shared test helper `styles/contrastSupport.ts` (resolves tokens and CSS fallbacks, composites translucent tints)
  - Verification: 516/516 frontend (`vitest`, 14 new in `systemStatusContrast.test.ts`, 1 fails against the old CSS), `tsc -b`, build and lint pass, mocked Playwright 82/82 (4 new real-browser stale-banner axe tests driving the page clock; they fail against the old CSS at 3.46:1), real-backend: Gate A scans 3/3, board 15/15, calendar 12/12, forms 10/10, workspace 14/14, STORY-003 7/7, scheduling 10/10, STORY-011 10/10, auth 12/12
  - Notes: colour-only; ALV-001-C01 (login banner) and ALV-N004 (backup warning text) are the next separate attempts

- [x] ALV-001-C01 R08: login banners meet WCAG AA in both themes (expired-session warning 3.16:1 and MFA success message; set to AWAITING_REVIEW, not COMPLETE)
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: `.alv-login__banner--warning` text uses `--color-warning-text`; new `.alv-login__banner--success` class (uses `--color-success-text`) replaces the inline style in `MfaSettingsPage.tsx`
  - Verification: 526/526 frontend (`vitest`, 10 new in `loginContrast.test.ts`, 4 fail against the old code), `tsc -b`, build and lint pass, mocked Playwright 90/90 (8 new real-browser banner axe runs; the warning runs fail against the old CSS at 3.16:1), real-backend: Gate A scans 3/3, board 15/15, calendar 12/12, forms 10/10, workspace 14/14, STORY-003 7/7, scheduling 10/10, STORY-011 10/10, auth 12/12 (incl. MFA confirmation)
  - Notes: colour-only plus one inline style moved to CSS; ALV-N004 (backup warning text and its intermittent stale-settings test) is the next separate attempt
