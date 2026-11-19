# ALV-N025 finding survey (step 1, read-only)

**Story:** ALV-N025 (plan item 62): access, audit, patient identity, import and merge integrity. It consolidates R05 PKG-10 (audit readability, observability, patient-access audit design) and PKG-11 (duplicate detection, import, merge, value lists, patient number), plus Q-5cl's access portion.
**Date:** 2026-11-19. **Method:** read-only code reading at `bb11744` (origin/main). No test was run and the database was neither read nor written. Each item's raw text is a line of the v6 log (`C:\.alvera-data\Manual-E2E\reviewer-package-v6\manual-e2e-findings.md`); the pre-implementation brief will carry the complete raw entries.
**Scope source:** the rows of `docs/stabilization/DISPOSITION_REGISTER.json` with `homePackage` PKG-10 (19 rows) or PKG-11 (35 rows: 15 findings and questions plus 20 observations O-G2a..O-G3i, which share log lines), and Q-5cl.
**Governance (R05):** PKG-10 group B (patient-access logging, PD-20) is a **compliance design: escalate**. PKG-11's patient number (PD-21) is a **data-model decision: escalate**. Each needs an approved design **before** any code for it. The plan text says this item "requires two-stage review" (briefing review and handoff review); the binding programme rule still defers full regression to ALV-N028.
**Unassigned findings that touch this area (candidates only; the owner assigns):** U-008 (dentists cannot import documents: permission default, ALV-N025/N026), U-011 (a follow-up names the login account instead of the person; the same root as group A1 below).

Verdict words: **CONFIRMED** (the code does what the finding says), **PARTLY FIXED** (a later story changed part of it), **NOT REPRODUCED IN CODE** (reproduce it in a test), **ANSWERED** (a question the code answers), **DECISION** (needs an owner decision), **OBSERVATION** (a positive or neutral note, kept for the record).

## PKG-10 group A: audit readability and observability

### A1. Who did it: one root cause
`ClinicalNames.ResolveAsync` resolves a person **only through a staff profile**; an account without one (for example `admin`) reads as the generic fallback "Staff member". Other screens do not resolve names at all and print ids. A single actor-name rule (staff display name, else the account's username, never a bare id, with the id kept for auditors) would answer F-029, F-138, F-149, F-172, F-214 and U-011 together.

| Id | Raw text (v6 log line) | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-029 (low-medium) | L138: fee history "by Staff member" instead of admin; two "1." lists; a different date format | `ProcedureCatalogService.Queries.cs` uses `ClinicalNames` -> fallback for an account with no staff profile | CONFIRMED | A1 rule; one list; one date format |
| F-138 (S4) | L877: document history "Opened by Staff member" (role name instead of the person) | `DocumentLibraryService.cs:104,110` uses `ClinicalNames` | CONFIRMED | A1 rule |
| F-149 (S3) | L987: audit "Performed by" shows raw user GUIDs; system actions "self" | `AuditLogPage.tsx:83` prints `performedByUserAccountId`; the `audit-log` API returns ids only | CONFIRMED | Resolve names in the audit view; keep the id (tooltip) |
| F-172 (S3), F-214 (S4) | L1129, L1289: Change history "By" shows `32d03dcb` | `PolicyHistoryPanel.tsx:38` `changedByUserId.slice(0, 8)` | CONFIRMED | A1 rule |
| F-087 (S3) | L355: "History of this encounter" lists events with times but no person | `EncounterView.tsx:234-241` renders event, detail, time; no actor | CONFIRMED | Show the actor (A1 rule); check the server view carries it |

### A2. What the audit says
| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-150 (S3), F-174 (S4), O-G2k | L988, L1138, L161: the heading says "account, role, and session events" but the log holds clinical, billing, document, report events; filters wanted | `AuditLogPage.tsx:50` description text unchanged | CONFIRMED | Retitle; add event-type and date filters |
| F-041 (medium) | L166: "Patient updated: phone." has no old and new values; the guarantor entry does not name the guarantor | `PatientEditService.cs:56` field names only; the audit is designed PHI-free | CONFIRMED | **DECISION (reviewer/owner):** before/after values conflict with the PHI-free audit; the R05 text suggests a link to the patient History row instead |
| F-036 (medium) | L160: the duplicate override does not say which patient it was compared against and has no reason | `PatientRegistrationService.cs:102` "Patient registered after reviewing possible duplicates." with no ids, no reason; the request carries `AcknowledgedDuplicateIds` | CONFIRMED | Record the acknowledged ids and a reason (needs a reason field: D-02a) |
| F-040 | L163: guarantor change audited (PASS); O-G3a same DOB not warned | Audited as "Guarantor set." | CONFIRMED (the detail gap belongs to F-041) | With F-041 |
| O-G2j | L161: household entries lack the relationship and the other party ("Household membership set.") | `PatientRelationshipService.cs:104` | CONFIRMED (observation) | With F-041 (ids and relationship, no names) |
| O-G2i | L161: blocked exact-duplicate attempts are not logged | No audit on the refusal path | OBSERVATION | Optional; owner choice |

### A3. Observability (logs)
| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-126 (S3), F-010 [DUP] | L802, L18: ~50 "Unhandled request exception: TaskCanceledException" for requests the browser abandoned | `SafeExceptionHandlingMiddleware.cs:35` logs every exception as an error with its type only | CONFIRMED | Treat a client abort (`OperationCanceledException` with `RequestAborted`) as information, not an error |
| F-127 (S2) [NOT_REPRODUCED] | L803: one "SqlException" with no number or message | Same line: no SQL number, method or path | CONFIRMED (the observability gap); the cause itself is not reproduced | Log method, path and SQL error number (never the message or data) |
| F-128 (S3) | L804: "appointment.flow measurement event was not recorded ... 'category' unacceptable" | `MeasurementEvent.cs:61` requires `^[a-z][a-z0-9_]{0,39}$`; success paths now convert (`AppointmentFlowService.MeasureCategory`), failure paths pass the raw refusal `code` | PARTLY FIXED | A test over every refusal code; normalise the failure path |
| F-212 (S4) | L1283: console lines have no timestamp | `appsettings.json` / `Program.cs`: no console formatter configured | CONFIRMED | A timestamped (or JSON) console formatter |

## PKG-10 group B: patient-access audit design (compliance; escalate)
| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-038 (HIGH, compliance) | L157: no patient-record access events (opening a workspace, viewing records); export/readable-record logging to check | No patient-view event exists; only DocumentOpened, ReportViewed and similar | DECISION (**PD-20**, owner with compliance) | A design document first: purpose, event shape, readers, retention, noise controls; no indiscriminate page-view logging |
| Q-5cf (compliance) | L991: same as F-038 | Same | DECISION (PD-20) | With F-038 |
| F-039 (medium) | L158: sign-out and session expiry are not recorded | Only LoginSucceeded/LoginFailed | DECISION (with PD-20) | `LogoutSucceeded`/`SessionExpired` after the design |

## PKG-11 group A: duplicate detection and override
| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-145 (S2/S3), F-210 (S3), F-211 [DUP] | L932, L1276, L1277: two Carla Chens with the same name, phone, email and address but a different birth year; neither import nor registration flagged them | `PatientDuplicateDetector.cs:35` compares only patients with the **exact** same date of birth | CONFIRMED | A lower-confidence rule: same normalised name plus a matching phone, email or address with a different DOB, in registration, import and the Duplicate patients page (thresholds decided in the brief); the guarantor picker shows enough to tell two apart (F-211) |
| F-148 (S3) | L959: "Not a duplicate" opens an unlabelled box with no instruction | `PatientMergePage.tsx` now has a **visually hidden** label (`alv-fu-sr`) and an aria-label on Confirm; there is still no visible label, no instruction and no required marker | PARTLY FIXED | A visible label "Reason (required)" and one line of instruction, like the merge form |
| D-02 / D-02a [DESIGN] | L145, L148: near-match override has no confirm or reason (D-02 revised) | "Register anyway" registers at once | DECISION | Owner: confirm step and/or a required reason for an override (pairs with F-036) |
| O-G2a..O-G2h, O-G3a..O-G3i | registration, near-duplicate panel, household, inactivate, child patient walkthroughs (mostly PASS) | as observed | OBSERVATION | Kept as evidence; O-G2f/O-G2g (F-037) belong to the closed PKG-02 |

## PKG-11 group B: import wording, merge, value lists, patient number
| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-147 (S2/S3) | L951: Import a patient refuses the package's `manifest.json`; it expects a single file | `PatientImportValidator.cs:25` takes only the single-file format (`format`, `formatVersion`, `exportId`, ...); the page says "a file exported from DentalKeep", although the package export cannot be imported | CONFIRMED | Wording names the exact file that can be imported (and that the package is for reading, not re-import), or accept the package: **owner choice** |
| F-144 (S3) [DESIGN] | L929: the dry-run Show filter takes one value at a time; owner wants multi-select | `ImportRowsTable.tsx:9` a single-value list | CONFIRMED (owner already asked for multi-select) | Multi-select |
| F-032 | L146: Sex is a free-text box | `PatientFieldsForm.tsx:34` text, `maxLength 30` | CONFIRMED | A fixed list with "unspecified"; migrate existing values (owner: the list) |
| F-033 | L150: State is free text; no Country field | `PatientFieldsForm.tsx:43` text, `maxLength 50` | CONFIRMED | A US list storing the 2-letter code; owner: US-only or Country |
| F-146 [DESIGN] | L933: no MRN / patient number | No number field anywhere (the id is a Guid) | DECISION (**PD-21**, escalate) | A design first: stable, unique, never reused, searchable, not the Guid, with migration, import and merge rules |
| F-202 (S4), Q-5bd | L1252, L751: a household of one whose only member is "Spouse" | Members can leave until one is left; nothing resets the relationship or dissolves the household | CONFIRMED (to reproduce in a test) | Review and fix the household-of-one behaviour |
| F-042, O-G3e..g | L167: inactivate uses the browser's own dialog | `PatientDetailsPanel.tsx:98`, `HouseholdGuarantorPanel.tsx:72` `window.confirm` | CONFIRMED | The app's own confirm dialog (R05: reviewed) |
| F-043, O-G3h/i | L168: child patient with a guarantor and household (PASS) | as observed | CONFIRMED (PASS) | Kept; nothing to fix unless the review says |
| Q-5cb | L957: merge shows only Phone under "Fields that differ" although the name and email differ | to check in `PatientMergeService` (the comparison set) | to verify in a test | Show every differing field and let the person choose (owner: S3 if confirmed) |

## Q-5cl (access portion)
| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| Q-5cl | L1147: does manager1 get a read-only or an editable Permission Matrix? (the Documents half goes to ALV-N026, PD-24) | `PermissionMatrix.cs`: OfficeManager has `ViewPermissionMatrix` but not `ManagePermissionGrants`; `PermissionsController` requires `ManagePermissionGrants` for every change | ANSWERED in code (read-only by the API) | A test that the screen offers no change to a manager and the API refuses one (acceptance: "Permission Matrix behavior equals actual API/UI access") |

## Decisions for the owner (before the brief)
1. **PD-20 (patient-access logging; compliance):** whether and how chart views, exports, prints and downloads are logged; who may read that log; retention; noise controls. Recommended: log patient-record **opens** (once per patient per session, not every page), exports, prints and downloads, readable by the audit-log role, kept as long as the audit log; plus sign-out and session-expiry events (F-039). A separate design document, approved before code.
2. **PD-21 (patient number):** a practice-assigned number, its format and length (the owner earlier proposed 8-10 digits), where it is shown, how it is backfilled, and what import and merge do with it. Recommended: a sequential number starting at 100001, unique and never reused, shown in the header, search, documents and exports, backfilled in creation order, kept by the surviving record on merge.
3. **D-02a (near-match override):** a confirm step and/or a required reason. Recommended: a required short reason, stored with the acknowledged ids (also answers F-036).
4. **F-041 (before/after in the audit):** the audit is PHI-free by design. Recommended: keep values out of the audit row and link it to the patient History row that holds them.
5. **F-147 (patient import):** fix the wording only, or also accept the package. Recommended: wording only now.
6. **F-032 / F-033 value lists:** the Sex list, and US-only or a Country field. Recommended: Female, Male, Other, Unknown/unspecified; US states plus DC and territories, US-only for now.
7. **U-008 and U-011:** whether to assign them to this story (U-011 shares the A1 root cause).
