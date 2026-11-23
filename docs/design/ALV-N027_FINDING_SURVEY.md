# ALV-N027 finding survey (step 1, read-only)

**Story:** ALV-N027 (plan item 64): messaging, follow-ups, reports, navigation, configuration and onboarding (R05 PKG-13 and PKG-14).
**Date:** 2026-11-23. **Session:** `CC-20261123-q4vn`.
**Method:** read-only code reading at `173a4dd` (origin/main `e07e137` plus one dev-script fix). No test was run, and no database was read or written.
- **Raw text:** each item's raw text is a line of the v6 log (`C:\.alvera-data\Manual-E2E\reviewer-package-v6\manual-e2e-findings.md`).
- **Scope source:** the 70 rows of `docs/stabilization/DISPOSITION_REGISTER.json` whose `homePackage` is PKG-13 (24 rows) or PKG-14 (46 rows), plus the plan text of both packages (`docs/design/MANUAL_E2E_STABILIZATION_PLAN_R05.md`, lines 436-515).

**Plan acceptance (item 64):**
- complete all mapped communication, follow-up, report, configuration, accessibility and usability corrections, **without turning messages into clinical documentation**;
- **resolve PD-04, PD-31, PD-34 and PD-37 before the behaviour that depends on them**;
- keep the DentalKeep public identity consistent;
- coverage: R05 stages 11 and 12, cases 9.01-10.05 and 12.01-12.03, and every mapped finding;
- two-stage review if the change risk needs a broad regression.

**Governance (R05):**
- PKG-13: none.
- PKG-14:
  - a large redesign is done **one pattern at a time**;
  - **security posture: escalate** (onboarding, F-016);
  - it touches every screen, so **the reviewer confirms the scope**.

**Verdict words:**
- **CONFIRMED** (the code does what the finding says);
- **PARTLY ADDRESSED**, **ADDRESSED** (already fixed by an earlier story);
- **ANSWERED** (a question the code answers);
- **DECISION** (needs an owner decision);
- **NOT YET CHECKED** (to verify at the start of its slice).

## Group A: notifications, messages and follow-ups (PKG-13 A)

**Root cause:** the lists and forms are correct, but they do not refresh after the user's own action, and they do not read as conversations or histories.

| Id | Raw text (v6 line) | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-134 | L833: after Mark all read, the count and the banner stay about a minute | `NotificationBell.tsx`: polls every 60 s (`NOTIFICATION_POLL_MS`) and on window focus. Nothing tells the bell that the page changed something | CONFIRMED | The notifications page signals a refresh (a shared event or context) after each of its own actions |
| Q-5bv | L897: the badge shows 1 again after "Nothing unread" | Same lag as F-134, or a new notice that had arrived | ANSWERED (F-134 lag, or a real new notice) | Covered by F-134 |
| F-139 (S3), F-167 (S4), F-168 (S4) | L895, L1109, L1110: tab toggles and notice buttons touch, light grey default buttons on the dark page, the pink urgent box is the loudest thing on every page | `NotificationsPage.tsx` uses the shared `Button`, but the toggles have no spacing. The banner is `.alv-bell-banner--urgent` with fixed light colours | CONFIRMED | Spacing, a theme-aware toggle, and banner colours from tokens (see group G) |
| F-169 (S3) | L1116: Hide is one-way; there is no Show hidden view or undo | Only read entries can be hidden; "nothing is ever deleted". There is no hidden list and no unhide in the API | CONFIRMED, **DECISION** | Owner: (a) a Show hidden view with Unhide, or (b) keep it one-way and say so clearly. Also a confirmation on Clear history (L915) |
| F-140 (S3) | L906: Reply does not pre-fill To with the sender | `ComposeMessage.tsx:12`: "a reply joins the thread; the writer picks the recipients again" | CONFIRMED | Pre-fill the original sender (plus "Reply all": an owner choice, recommended no) |
| F-141 (S3) | L907: the thread does not read as a conversation | `MessagesPage.tsx:96`: plain stacked entries | CONFIRMED | A conversation layout (own messages on one side, sender and time on each), plain text only |
| F-142 (S3) | L908: the Sent list shows "to 1, read by 0" | `MessagesPage.tsx:54`: numbers only. The thread view already names each recipient and when they read it (`:102`) | CONFIRMED, **DECISION** | Owner: names in the Sent row (up to N, then "+k"), or keep the numbers with a hover |
| Q-5bw | L902: an empty subject is accepted, shown as "(no subject)" | Subject is optional | **DECISION** | Owner: keep it optional (recommended), or require it |
| Q-5by | L914: does a message to dentist1 create a notice? | `MessageService.cs:92` adds a `MessageReceived` notice for **every** recipient | ANSWERED (yes) | Recheck in the walkthrough only |
| F-143 (S3) | L920: the data-import batch says "by Unknown staff member" while the admin is signed in | `ImportIntakeService.cs:137` looks up the **staff profile only**; an administrator with no profile falls back. The same bypass of ALV-N025's actor-name rule (`ClinicalNames.ResolveAsync`: display name, else username) is in `ExportHistoryService.cs:40` and `AuthController.cs:307` | CONFIRMED (3 places) | Use the shared rule in all three |
| F-132 (S4) | L822: "No open follow-ups" with nothing under Closed for system tasks; they appear only in the timeline | `FollowUpsPanel.tsx:93`. The patient's Follow-ups list shows tasks, but the auto-closed system tasks were not listed (to confirm: a filter on kind or owner) | CONFIRMED (to confirm the exact filter) | List the system tasks in Closed and All, or explain them |
| F-135 (S3) | L843: after removing the opt-out, the stale "Reason: Test: opt out" stays | `PatientRecallCard.tsx:49` pre-fills the old reason, and saving sends it back. `RecallAdminService.cs:91` stores it | CONFIRMED | Clear the reason when the opt-out is removed (client and server), or label it per state |
| F-136 (S3) | L844: recall changes are not in the Follow-ups timeline, though the page says every action is recorded | Recall changes **are** audited (`RecallPlanChanged`, `RecallAdminService.cs:93`). The timeline (`FollowUpService.WorkQueue.cs:80`) reads follow-up task events only | CONFIRMED | Show recall-plan changes in the timeline, from the audit or a small history table. A table needs a migration (brief decides) |
| Q-5bo | L825: the follow-up-due notice went to manager1 only, and was not marked read when the task was completed | The reminder goes to the task's owner. Completing a task does not touch its notice | **DECISION** | Owner: should completing a task mark its own due notice read (recommended yes) |
| Q-5bx | L910: does a due follow-up re-notify every few minutes? | No. One notice per task, due time and recipient (`FollowUpRules.ReminderSourceKey`, unique per recipient). A second notice means another task or a changed due time | ANSWERED | None. Say so in the help text |
| Q-5br | L872: lab case history says "Edited" but not what changed | `ExternalCaseService.cs:103, 127`: an `Edited` event with no detail | CONFIRMED | Name the changed fields, never their values (as ALV-N025 did for patient edits) |
| Q-5bt | L884: after a remake, "Went out" keeps the first date and "Came back" is not cleared | `ExternalCaseService.Status.cs:36`: going out again sets `SentOn` to the new date and clears `ReceivedOn` | ANSWERED by the code (likely a screenshot taken before reload; recheck in the walkthrough) | Walkthrough check |
| Q-5bu | L885: is the session lifetime fixed rather than sliding? | `Program.cs:324`: `SlidingExpiration = false`. A fixed lifetime, by design (ALV-N025 built the session audit on one deadline) | ANSWERED (fixed, by design) | None. Changing it is a security-posture decision, outside this story |

## Group B: reports and measurement (PKG-13 B)

| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-216 (S3) | L1306: saving the same baseline twice gives two identical rows, which cannot be removed | `MeasurementBaselineService.cs:23-89` is idempotent **per Idempotency-Key** only. Two saves with fresh keys make two baselines | CONFIRMED | Refuse (409) a second baseline with the same name, period and filters, or return the existing one. A unique index needs a migration; existing duplicates are kept and disclosed |
| F-217 (S4) | L1307: the overlap message is hard to read | `MeasurementComparisonService.cs:22` | CONFIRMED | The tester's wording: "Your baseline period must end before the period you are comparing it to" |
| F-157 (S3) | L1057: permission calls taking about 4,000 ms | Not reproduced in the manual pass | NOT_REPRODUCED in the register | Re-measure once in the walkthrough. A latency budget test only if reproduced |
| Q-5ce | L971: "Clinical notes not finalized 0 of 0" although notes exist in range | `MetricDefinitions.cs:29-45`: only notes at least 7 days old (`NoteGraceDays`) are counted, so recent notes give 0 of 0 | ANSWERED | Say so on the metric ("notes at least 7 days old"), and make "Not available" explain why |
| case 12.02 | Report totals reconcile with their transactions | ALV-N024 worked on reconciliation | NOT YET CHECKED | Reconciliation test in the slice |

## Group C: onboarding and user details (PKG-14 A), escalation

| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-016 (Q) | L24: there is no screen to create a staff account | `AuthController.cs:43`: an **anonymous** `POST /api/auth/register` (rate-limited, `AuthAttempts`). No "Add user" in Security Administration, and no forced password change | CONFIRMED, **DECISION PD-04, security escalation** | Owner and reviewer: in-app Add user (name, role, temporary password, enabled, change at first sign-in); the open register endpoint gets an off switch (recommended: off by default once one administrator exists) |
| F-018 (S3) | L26: the User details page has no way back | `pages/AdminUserDetailPage.tsx`: no back link | CONFIRMED | "Back to users" |

## Group D: menu visibility and report filters (PKG-14 B)

| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-022, F-031, F-159 (Showcase) | L30, L142, L1065: Component Showcase is in every role's menu | `moduleRegistry.ts:23-29` (ALV-N021 A2, F-064): only in a development build | ADDRESSED (a production build has no Showcase) | A menu-per-role test |
| F-022, F-159, O-G1b (System Status) | L30, L1065, L142: System Status is visible to every role | `moduleRegistry.ts:45`: `requiredRole: "Admin"`, and ALV-N026 made its sections administrator-only | ADDRESSED (to confirm the API side for non-admins) | Menu-per-role test, and a direct API check |
| F-031 | L142: the menu shown to each role must match the matrix | Entries carry `requiredPermission`/`anyPermission` | PARTLY ADDRESSED | Table-driven test per role (chain row 1.07) |
| O-G1a | L142: a message names the internal permission code | NOT YET CHECKED | NOT YET CHECKED | Find and reword |
| F-158 (S3) | L1064: the Schedule report says providers could not be loaded for frontdesk1 | `ReportsHubPage.tsx:59` calls `GET /api/config/providers`, which needs **ManagePracticeConfiguration** (`ConfigurationController.cs:250-251`) | CONFIRMED | A read-only provider list for report viewers (the scheduling list, or a narrower endpoint) |

## Group E: navigation, layout, wording and visual consistency (PKG-14 C)

**Root cause:** patterns were built page by page. 59 page files render raw `<button>` and 81 raw `<input>` elements rather than the shared themed components, so the dark theme misses them. The dashboard is still ALV-N001's placeholder.

| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-008 (S2) | L16: the Dashboard is the ALV-N001 shell placeholder | `pages/DashboardPage.tsx:5-17`: "No feature modules have been built…" | CONFIRMED, design inside the story | A real dashboard (the brief proposes its content: today's schedule, my follow-ups, unread messages, alerts, by permission) |
| F-034 (medium) | L151: the always-visible patient bar is not context-aware | `app/PatientHeader.tsx` | **DECISION** | Owner: hide the bar on admin, report and registration pages (recommended) |
| F-035 (medium) | L152: Register patient shows the previously selected patient | Same | CONFIRMED | Clear or hide the selected patient on registration |
| F-075 (S3) | L291: no busy indicator on filters and other actions | Per page | CONFIRMED (pattern) | One busy pattern on the shared Button and on list reloads |
| F-083 (S2) | L331: the workspace has no appointments view | NOT YET CHECKED in code | CONFIRMED in the raw log | A workspace Appointments tab with a rebook link |
| F-096, F-176, F-194, D-01 (S4/S3) | L436, L1143, L1227, L456: white inputs and grey default buttons in the dark theme | `styles/global.css` styles only `textarea` globally; the raw controls above miss the tokens | CONFIRMED | Global themed base styles for input, select, textarea and button (one change, then page checks) |
| D-02 | L463: workspace tabs look like plain text | Tabs have no shape | **DECISION (design)** | One tab pattern |
| D-07 | L1197: group the workspace tabs (Diagnoses and Treatment plan under Clinical) | | **DECISION** | Owner approves the grouping |
| D-08 | L1218: stop showing blank create forms all the time | e.g. the "Create treatment plan" form | **DECISION** | "Add …" button that opens the form (recommended) |
| D-04 | L610: two prescription entry paths (quick entry and composer) | `docs/PRESCRIPTION_WORKFLOW.md` | **DECISION** | A complete prescription workflow. **Large; recommend a separate story** (it touches ALV-N026's prescription work) |
| D-05 | L945: menu organisation and the data-exchange entries | `moduleRegistry.ts`: 30 flat entries | **DECISION** | Grouped menu (owner approves the groups) |
| E-01 | L443: tooth drawings instead of tiles | Licence check pending (memory: owner's reference site) | **DECISION** | Recommend outside this story |
| F-160 (S4) | L1075: a not-found id says "could not load" with a pointless Retry | NOT YET CHECKED (which page) | CONFIRMED in the raw log | "Not found" without Retry |
| F-161 (S3) | L1079: on phones and at 200% zoom the menu, theme button and banner push the page down | Layout | CONFIRMED | A collapsible menu at narrow widths |
| F-162, F-163 | L1080, L1081 | | NOT_REPRODUCED in the register | Recheck at real widths in the slice |
| F-200 (S4) | L1246: tabs tell the user to go to another tab with no link | Per tab | CONFIRMED | "Go to …" links |
| F-213, F-215 (S4) | L1288, L1290: the permission matrix scrolls sideways, and every cell repeats "default" and Change | `PermissionPolicyScreens` | CONFIRMED | A sticky header and caption; mark only non-default cells |
| PD-34 | Wording "Deactivate"/"Inactive" instead of "Inactivate" | Used across the app (patients, documents, staff, procedures) | **DECISION PD-34** | Owner approves the wording, then one sweep |

## Group F: practice configuration (PKG-14 D)

| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-019 (S3) | L27: Time zone and Currency look editable | `PracticeTab.tsx:110-111`: `readOnly`, but styled like an editable box | CONFIRMED | Read-only styling for the shared FormField |
| F-020 (S2) | L28: Add location opens the form below the list, out of sight | `components/ConfigEntityPanel.tsx`: no scroll or focus on open | CONFIRMED | Scroll the form into view and focus its first field |
| F-023 (Q) | L31: any active staff member can be made a provider | | **DECISION PD-31** | Owner: which roles may be providers |
| F-024 (Q) | L32: Specialty is free text | | **DECISION PD-31** | Owner: a fixed list (recommended: the ADA specialties plus General dentistry and Hygiene) |
| F-025, O-E6a, O-E6b | L130-131: the window row error stays after a revert; Save is enabled for an invalid row | `AvailabilityTab.tsx` | CONFIRMED | Clear the error on change, revert and save |
| F-026 | L133: "Scheduling preview" shows only configuration, not blocked time | `SchedulingPreviewTab.tsx` | **DECISION** | Owner: show blocked time, or rename the tab |
| F-027 | L147: "Add a procedure" sits at the end of the list | `ProcedureCatalogPage` | CONFIRMED | Move the button to the top |
| F-028 (low) | L137: the duplicate-code banner says "the Local code system" while the form says "Practice code" | `ProcedureCatalogService.cs:133` prints the raw `CodeSystem` value | CONFIRMED | Use the display name |
| O-E7a, O-E8a, O-E8c, O-E9a, O-E10a, O-E8b | L130-140: observations (three practice names, help text that stays, CDT wording, registration requirements, fee history time) | | OBSERVATION | Fix where cheap; O-E8b is Q-5bu (fixed session lifetime) |

## Group G: date-format preference (PKG-14 E)

- **What the code shows:** a shared formatter exists (`time/practiceTime.ts`, `components/dateOnly.ts`), but **21 client files** still call `toLocaleDateString`, `toLocaleString` or `Intl.DateTimeFormat` directly. PDFs format dates in their Typst templates and the server.
- **Decision PD-37:** the owner chooses the options, the default, and the print default. Recommended:
  - YYYY-MM-DD, MM/DD/YYYY and DD/MM/YYYY;
  - default MM/DD/YYYY (US practice);
  - printed clinical documents use an unambiguous form ("14 Mar 1985").
- **Build:** a practice setting (a migration and an audit event); every screen goes through the one formatter; PDFs follow the print setting.
- **Note:** the PDF templates are SHA-pinned (ALV-N026), so changing them changes the reviewed pins.

## Decisions needed before the brief

| # | Decision | Blocks |
|---|---|---|
| PD-04 | Onboarding: in-app Add user, temporary password, forced change; the register endpoint's off switch. **Security posture: escalate** | Group C |
| PD-31 | Provider eligibility (which roles) and the specialty list | F-023, F-024 |
| PD-34 | "Deactivate"/"Inactive" wording everywhere | Wording sweep |
| PD-37 | Date-format options, default and print default | Group G |
| N27-1 | Scope: R05 says PKG-14 "touches every screen; the reviewer confirms scope". **Recommendation:** keep D-04 (prescription workflow unification) and E-01 (tooth drawings) out of ALV-N027, as separate stories; do the dark-theme fix as one global base-style change | Brief scope |
| N27-2 | F-169: Show hidden with Unhide, or one-way with a clear explanation (and a confirmation on Clear history) | Group A |
| N27-3 | F-142: names in the Sent list, or numbers with a hover | Group A |
| N27-4 | Q-5bo: completing a task marks its own due notice read | Group A |
| N27-5 | Q-5bw: is a subject required | Group A |
| N27-6 | F-034: hide the patient bar on admin, report and registration pages | Group E |
| N27-7 | F-026: show blocked time, or rename the tab | Group F |
| N27-8 | D-02, D-05, D-07, D-08: tab look, menu groups, workspace tab grouping, add-buttons for create forms | Group E |
| N27-9 | F-008: dashboard content | Group E |

## Next step
After the owner's decisions: the pre-implementation brief (with a source appendix and an owner decision record, in the ALV-N025/N026 shape), sent for independent review. **No code before the brief is approved.**
