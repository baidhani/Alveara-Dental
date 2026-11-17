# ALV-N022 A3: Step 0 verification record and briefing addendum 01

**Story:** ALV-N022 (plan item 59), attempt A3: display and integrity (briefing revision 3, sections 2 and 2.1; scope matrix A3 rows; OD-07..OD-09).
**Status:** documents only. Nothing built. A1 and A2 are COMPLETE (closures `2690d7a`, `c2d6959`). Per review A2-R02-STAGE2, A3 needs its own current Step 0/briefing review before implementation; this record asks for it.
**Method:** read-only code search on `c2d6959`. No database access. The register's and R05's item texts are truncated excerpts; the raw manual log (`manual-e2e-findings-2026-10-30.md`) is not in the repository, so where a truncation hides the complaint it is said.

## 1. Classification of every A3 item

| Item | Observed (register) | Code today | Result | A3 change if approved |
|---|---|---|---|---|
| **F-106** (0.7) | Prescriptions tab lists both versions identically, no status, instructions or quantity | The "Prescriptions" tab (`App.tsx:255`, STORY-016) renders `PrescriptionList` from `PrescriptionService.ListAsync` over the base `Prescriptions` rows (`PrescriptionService.cs:37`); its view (`PrescriptionViews.cs:15`) has no status, instructions, quantity or supersede link. Those exist in the ALV-016-C01 workflow tables (`PrescriptionWorkflow.cs:16-29, 50-52`) and the workflow's own `PrescriptionRecordList` already shows them (separate "Prescription composer" tab) | CONFIRMED; **no new field needed** | Additive read-only fields on the tab's view joined from the workflow record (status, instructions, quantity, "superseded by"/"supersedes"); a row with no workflow record says so |
| **F-105** wording (0.8) | "No recorded match" read as a verdict | Every surface already says it is a name comparison and not a clearance: server check (`AllergyCheck.cs:71`), saved-result words (`prescriptionText.ts:6-11`), composer/finalize (server text), print (`PrescriptionLifecycleService.cs:79`) | CONFIRMED as compliant | Only the briefing's guard test: no allergy-check surface may contain verdict words ("safe", "cleared", "no allergy" without "not a clearance") |
| **F-067** (+F-124) | Sex shown as "male" for one patient, "Female" for another | `PatientHeader.tsx:51` prints the stored value as is; imported rows hold lowercase | CONFIRMED | Display the known values in one form (stored value unchanged) |
| **F-079** | Each item's History expanded by default | `ItemHistory.tsx:36` and `EncounterView.tsx:234` are closed `<details>`; unchanged since 2026-10-03 (before the 2026-10-30 pass) | **NOT_REPRODUCED** | none - see Q-1 |
| **F-082** | Encounter row "Some sections not yet addressed - 0 entries" with vitals recorded | `EncounterReader.cs:74-75` counts documentation entries and chart changes only; vitals and notes are not counted, so the label is ambiguous | CONFIRMED | Label says what it counts (for example "0 history/allergy/medication entries") |
| **F-089** | Empty Source saved as "Source: patient" | Source is required on both sides (`SafetyForms.tsx:85`, `SafetyRules.cs:33`) and shown as stored (`SafetyEntryRow.tsx:47`); record-derived entries show "Clinical record: ..." (`SafetyContextService.cs:82`); no "patient" default anywhere; unchanged since ALV-N011 (2026-10-03) | **NOT_REPRODUCED** | none - see Q-1 |
| **F-090** (0.10) | Clearance "Document reference" is free text | `Clearance.DocumentReference` is `string?` free text (`SafetyEntities.cs:93-94, 121`), documented as "free text until documents exist" | CONFIRMED; **DESIGN_DECISION_REQUIRED** | Picker over the patient's own non-deleted documents - storage choice Q-2 |
| **F-092** | Withdraw wording ("Use this for an entry that was wrong. It leaves the chart ... re...", truncated) | Hint at `FindingRow.tsx:63`; the text shown reads correctly; the complaint itself is cut off in the register and R05 | **SOURCE NEEDED** | Q-3 |
| **F-093** | Stale tooth message after selecting another tooth | `OdontogramPanel.tsx`: the save message (`setSaving`) is not reset when a different tooth is selected (`ToothChart onSelect`) | CONFIRMED | Clear the message on tooth change |
| **F-094** | Identical repeat gives no message | Server: identical active finding is a quiet repeat returning the chart (`OdontogramService.cs:24, 127`); client then shows its normal "recorded" text, unchanged from before | CONFIRMED | Client compares before/after and says "already recorded" (no server change) |
| **F-095** (0.11) | Plan/Complete are only labels | Plan and Complete move a finding's chart state only (Diagnosed -> Planned -> Completed, `OdontogramService.cs:80`); they create no plan item or completion record | CONFIRMED: **labels only** | Explain-only text on the odontogram (no connection built) |
| **F-097** | Perio site boxes have no visible labels | `PerioGrid.tsx:65-67` inputs have screen-reader labels only | CONFIRMED | Visible labels (depth / recession) |
| **F-098** (0.10) | Perio "Linked records" reference is free text | `PerioLink.LinkType` + `Reference` string (`PerioSessionEntities.cs:81-88`), "opaque" by design | CONFIRMED; **DESIGN_DECISION_REQUIRED** | Picker over the same patient's diagnoses - storage choice Q-2 |
| **F-099** (+F-104) | Same tooth shown in two numbering systems | `DiagnosisHistory.tsx:46` and `ClinicalHistorySection.tsx:34` print the stored FDI key raw; other places use the display helper | CONFIRMED | Use the screen's numbering everywhere (stored key unchanged) |
| **F-137** | Tooth 99 not flagged on the lab-case form | Server refuses it (`ExternalCaseValidation.cs:44-48`, `tooth_invalid`) but reports one problem at a time and the form validates "laboratory" locally first, so the tooth is never sent/flagged | CONFIRMED | Client checks the tooth field too |
| **F-199** | "Medical history have not been reviewed" | `SafetyContextService.cs:119` uses "have" for every section label | CONFIRMED | Agreement per label ("has" for singular labels) |
| **F-091, Q-5v, Q-5w** (0.12) | Missing-tooth display | Each finding row already shows its recorded status as a chip (`FindingRow.tsx:40`); the tile shows number, most urgent state and a count, and **draws no "absent" glyph for any status** (`ToothChart.tsx:26-32`) | Display already as 2.1 requires; **glyph part dropped** (2.1) | none (record only); PD-09d semantics stay OPEN |
| **Q-5aa** | Diagnosis card does not show a linked perio chart | Two link systems: `DiagnosisLinks` (diagnosis -> finding / PerioExam, shown as chips) and `PerioLinks` (chart -> diagnosis by free text). A link made from the chart side is invisible to the card | CONFIRMED; **depends on F-098 (Q-2)** | Card lists perio charts linking to it once the reference is a real id |
| **Q-5ai** | Tooth not pre-filled from the chosen diagnosis | `TreatmentPlanItemFields.tsx:38` shows the diagnosis's tooth in the option but does not pre-fill the Tooth field | CONFIRMED | Pre-fill when the Tooth field is empty |
| **Q-5j** | Templates page under a patient URL | `App.tsx:261` nests `clinical/templates` under `/patients/:id`; ALV-005-C01 fixed only the API location and permission (`docs/CLINICAL_DOCUMENTATION.md:142-143`), not the page URL | CONFIRMED | Practice-level route; the old patient URL redirects |
| **Q-5aj** (0.13) | After completing L3000 on tooth 14 M, the odontogram shows 14 Mesial "none recorded" | By the approved ALV-008-C01 rule a completion updates only a chart finding linked through its plan's diagnosis on the same tooth and surface; it never creates one. With no linked finding, nothing changes - the display is correct for the rule | **Belongs to the completion rule (PKG-08 territory)** | Stop: Q-4 |

## 2. Stop conditions met (briefing section 3)
- **NOT_REPRODUCED:** F-079, F-089 (Q-1).
- **Complaint not recoverable from the repository:** F-092 (Q-3).
- **Schema/storage choice (0.10):** F-090, F-098, and Q-5aa through F-098 (Q-2).
- **Another package's rule (0.13):** Q-5aj (Q-4).
All other items are independent of these and can proceed when A3 is authorized.

## 3. Questions for the reviewer
- **Q-1. F-079 and F-089.** Recommended: record them as NOT_REPRODUCED in the register (with this evidence) and build nothing; reopen only if the owner's raw log shows a screen this search missed.
- **Q-2. F-090 and F-098 storage.** Option **A** (recommended): keep the existing string columns; the picker stores the chosen record's id there; the service validates on write that it is a non-deleted document (F-090) or a diagnosis (F-098) of the same patient; a value that is not such an id is shown as "free-text reference (legacy)". No migration; integrity is service-enforced (no foreign key). Option **B**: add a nullable id column with a foreign key beside the legacy text (a migration and its up/down proof). Q-5aa follows the choice.
- **Q-3. F-092.** The owner supplies line 423 of the raw manual log, or the item is recorded as NOT_REPRODUCED.
- **Q-4. Q-5aj.** Recommended: no rule change in A3 (completion creating chart findings would change the ALV-008-C01 rule); optionally add one explanatory sentence in the completion result ("No chart finding was linked to this procedure, so the chart was not changed"). Otherwise defer to a completion story.

## 4. A3 plan if approved (focused, each group about 1-2 min)
- Server: F-106 additive view fields; F-199 grammar; F-090/F-098 per Q-2 (validation and legacy display); the F-105 guard test.
- Client: F-067, F-082, F-093, F-094, F-095 text, F-097, F-099, F-137, Q-5ai, Q-5j route and redirect, Q-5aa (per Q-2), F-106 display.
- Tests: one unit or component test per display row (briefing section 10 A3 row), the verdict-word guard, F-090/F-098 same-patient refusal and legacy display, pickers within their scope.
- Negative control from the briefing: "safe" planted in the no-match text (must fail the guard). Plus one for the picker's same-patient check.
- Two-stage: Stage 1 focused proof; Stage 2 as the reviewer defines. `fullRegression: DEFERRED_TO_ALV-N028`.

## 5. Disclosure
Read-only code search only; no database query, no build, no test run.

---

## 6. Addendum 02 (answers review `.alveara/reviews/ALV-N022/A3-STEP0-ADDENDUM-01.md`, CHANGES_REQUIRED: CR-1, CR-2, D-1..D-3)

### 6.1 Owner decisions (2026-11-17)
- **D-1: Option B** - typed nullable link columns with foreign keys (reviewer's recommendation).
- **D-2: F-092 NOT_REPRODUCED** with the section 1 evidence; reopened only if the owner supplies raw-log line 423 or a reproducible screen.
- **D-3: add only the neutral sentence** to the completion result.

Q-1 (F-079, F-089 NOT_REPRODUCED) was accepted by the review. Q-5aj creates no finding and changes no completed-procedure rule.

### 6.2 CR-1: F-091 absent-glyph rule restored (my error in section 1)
Section 1 wrongly treated "no glyph is drawn today" as "already satisfied"; the briefing and the plan require the bounded display correction to be **built**. A3 implements and tests exactly:
- each finding row shows its recorded status (already true; kept and tested);
- the tooth tile draws an "absent" mark **only** when the tooth has an active missing-tooth finding (condition type whose `toothEffect` is `Absent`) in state **Existing or Completed**; a Diagnosed or Planned missing finding shows its status but no absent mark;
- an implant and a missing finding are both listed as recorded: no superseding, no "replaced by", no requirement either way;
- the mark has a text alternative in the tile's accessible name ("not present").
No missing-tooth clinical meaning, extraction intent or implant relationship is inferred; the recording rule (`tooth_absent` refusal for Existing/Completed) is unchanged. PD-09d semantics stay OPEN.

### 6.3 D-1 design (Option B)
**Migration (one):**
- `Clearances.DocumentId` and `ClearanceVersions.DocumentId`: nullable `uniqueidentifier`, foreign key to `PatientDocuments(Id)` (no cascade). The version table stays append-only and snapshots the id.
- `PerioExamLinks.DiagnosisId`: nullable `uniqueidentifier`, foreign key to `Diagnoses(Id)` (no cascade); set only on insert (the table is append-only, `TR_PerioExamLinks_Immutable`). Used when `LinkType = Diagnosis`; the other link types keep the reference text.
- Same-patient enforced in the database by two AFTER INSERT, UPDATE triggers: a clearance's document must belong to the clearance's patient (THROW **51288**); a perio link's diagnosis must belong to the link's patient (THROW **51289**). Both numbers verified unused.
- Existing rows untouched: legacy `DocumentReference` / `Reference` text stays and is shown as "free-text reference (legacy)"; nothing is converted or guessed.
- Down: drops the triggers, the foreign keys and the columns; refused (THROW) if any row already holds an id, so no link is silently lost.

**Service:** the picker sends an id; the service checks it exists, belongs to the same patient and (documents) is not deleted/inactive, and refuses otherwise with a field error; the database triggers back this up. The free-text field is no longer offered for new entries.

**Client:** F-090 picker over the patient's own active documents; F-098 picker over the same patient's diagnoses; legacy text shown read-only and labelled. **Q-5aa** then lists, on the diagnosis card, the perio exams whose `DiagnosisId` is that diagnosis.

### 6.4 D-3
When a completed procedure's chart outcome is "no linked finding", the completion result adds: "No chart finding was linked to this procedure, so the chart was not changed." No other change.

### 6.5 Revised A3 plan and test matrix (Stage 1)
| Item | Server test | Client test |
|---|---|---|
| F-106 | tab view carries status, instructions, quantity, supersession from the workflow record; a row without one says so | rows show them; superseded marked |
| F-105 | guard: no allergy-check surface contains verdict words | - |
| F-199 | "has"/"have" per label | - |
| F-090 / F-098 (D-1) | same-patient refusal (service), direct-insert refusal 51288 / 51289 (database), legacy text kept; migration up/down and refused down with ids present | pickers list only the patient's own records; legacy shown read-only |
| Q-5aa | card read lists linked exams | card shows them |
| F-091 (CR-1) | - | glyph only for Existing/Completed missing; Diagnosed/Planned show status, no glyph; implant + missing both listed |
| F-067, F-082, F-093, F-094, F-095, F-097, F-099, F-137, Q-5ai, Q-5j, D-3 sentence | - | one focused case each (Q-5j: old URL redirects) |

**Real-browser workflow (CR-2):** one new fresh-database real-backend Chromium spec, about **1.5-2 minutes** including the build (under five; ask first if a run exceeds it): (1) the Prescriptions tab shows a workflow prescription's status, instructions, quantity and its superseded version marked; (2) the odontogram shows a Diagnosed missing finding with its status and no absent mark, then an Existing one with the mark; (3) the perio linked-record picker offers only the patient's diagnoses, and an API attempt with another patient's diagnosis is refused.

**Negative controls (restored with a hash check):** "safe" planted in the no-match text (must fail the guard); the service's same-patient check removed (the database trigger must still refuse - proves the layering); the glyph drawn for every status (must fail the glyph test).

**Route:** Stage 1 focused proof as above; Stage 2 as the reviewer defines after Stage 1 approval. Broad regression `DEFERRED_TO_ALV-N028`. No external medication service, no allergy-class, interaction, dose or compatibility logic.
