# ALV-N022 pre-implementation briefing (revision 3): safety and clinical-record integrity, finalized encounters

- **Revision 3 (2026-11-16):** answers `.alveara/reviews/ALV-N022/BRIEFING-R02.md` (BR-7, BR-8): documentation corrections to the Execution Plan, R05 and the register only. Sections 1-12 are unchanged from revision 2 (`48967b2`).

- **Story:** ALV-N022 (execution item 59): R05 **PKG-06** and **PKG-07**.
- **Date:** 2026-11-16. **Session:** CC-20261115-m7x2. **Code read at:** `2eca1a9`.
- **Revision:** 2, answering `.alveara/reviews/ALV-N022/BRIEFING-R01.md` (CHANGES_REQUIRED). Revision 1 is in `0a19afc`.
- **Status:** **PROPOSED, awaiting independent briefing review.** This is a document only. No product code, test, schema, migration, setting, permission or external integration has changed. The only command run was the repo's document checks (`node --test tests/*.test.mjs`, 53/53, 2 s) after the plan, R05 and register edits. No focused, browser, chain, broad or full test was run.
- **Companion files (this package):**
  - `ALV-N022_OWNER_DECISION_RECORD.md` (rev. 2)
  - `ALV-N022_SCOPE_MATRIX.md` (rev. 2)
  - `ALV-N022_FINDING_SURVEY.md`
  - `ALV-N022_DECISION_REQUEST.md`, superseded by the record where they differ
  - Amended authorities: `GPT Docs/Alveara_Dental_Execution_Plan_v4.md` (item 59), `docs/design/MANUAL_E2E_STABILIZATION_PLAN_R05.md` (PKG-06 Group C, PD-12, row 6.07), `docs/stabilization/DISPOSITION_REGISTER.json` (F-105 note)
- **Not authorized before approval:** implementation, migration, external lookup, allergy-class logic, or any test run beyond the document checks.

## 0. Answers to BRIEFING-R02 (revision 3, documents only; no test command run)

| Item | Answer |
|---|---|
| BR-7 (row 6.07) | R05 chain row 6.07 now asserts only that a direct recorded-name match shows the existing warning with the existing override reason, and that a no-match result is never presented as a safety, compatibility, approval or clearance verdict. The not-checkable expectation is moved entirely to the deferred Medication Reference and Provider Lookup follow-on and is not asserted in the chain. |
| BR-8 (F-091) | Execution plan item 59 no longer requires "missing-tooth invariants". It states that the clinical meaning of Diagnosed/Planned missing teeth, extraction intent and implant/missing relationships is OPEN, deferred for a future owner decision (PD-09d), that ALV-N022 makes only the non-semantic display correction after Step 0, and that it cannot claim to resolve the invariant. The same amendment is in R05 PKG-06 Group B and chain row 5.03 (only the Existing/Completed refusal and the status display are asserted), and appended to the register's F-091 note (disposition unchanged). |

## 0a. Answers to BRIEFING-R01 (revision 2)

| Item | Answer |
|---|---|
| BR-1 (authorities) | Plan item 59 Scope and Acceptance are amended (F-105/PD-12 DEFERRED, not removed; only the direct-name warning; no class seed, drug API, interaction, contraindication, dose, compatibility or prescribing-decision logic; follow-on Medication Reference and Provider Lookup, its own design, no patient data external; a **release limitation**, not claimed as delivered). R05 PKG-06 Group C has the same amendment. The PD-12 row and chain row 6.07 are marked deferred. The register's F-105 note records it; its disposition stays `CONFIRMED`, because `DEFERRED` is not in the register vocabulary and changing the vocabulary is a schema change not asked for. |
| BR-2 (decision authority) | Decision record rev. 2: column 2 quotes only the owner's words. Acceptance criteria are labelled as the agent's proposals. No clinical sign-off is claimed. The rev. 1 signature is explained, and a fresh owner line covers only the DECIDED rows. OD-10..OD-15 stay PROPOSED. **OD-13 (signed drafts) is removed from A2 and from its tests**: signed drafts keep today's behaviour. |
| BR-3 (dental history) | Option 1: dental history is **removed from A1**. Its model, routes and behaviour are unchanged (4.1). |
| BR-4 (duplicates) | The new normalized active-duplicate refusal applies to **allergies only**. Other kinds keep their current duplicate semantics. Category-boundary tests are added (10). |
| BR-5 (legacy add) | The "add to chart" flow is an explicit confirmation flow (5.3). It is built only if the owner approves OD-14. Otherwise legacy entries are display-only in A1. |
| BR-6 (truthful gates) | The matrix says PLANNED and gives each row's verification level. Step 0 enumerates every register-only item with a stop rule (3). The F-095 and F-090/F-098 picker scopes are fixed in the matrix. |

## 1. Problem in one paragraph

Allergies, medications and medical history exist twice: on the chart (`ClinicalRecordItem`) and in each encounter (`EncounterEntry`).
- The banner, the Safety tab and the prescribing allergy check read only the chart.
- Encounter entries are checked for duplicates only within their own encounter, and never reach the chart.
- The banner refreshes only on an event the Safety tab sends.

This explains F-078, F-080, F-084, F-085, F-086, F-111 and F-112 (survey section 1). The chart side already has what the fix needs:
- `ClinicalRecordService.AddItemAsync`, `UpdateItemAsync` and the section review take an `encounterId`.
- Versions and section events carry `EncounterId`, and triggers keep them append-only.
- Duplicate live items are refused, race-guarded by a unique index.
- "None known" is refused when the section has items.

So A1 is mostly **routing** the encounter's allergy, medication and medical-history actions to the chart service.

## 2. Attempts

| Attempt | Content | Decisions |
|---|---|---|
| **A1** chart as single source | Encounter allergy, medication and medical-history actions write to the chart with provenance; the encounter reads those sections from the chart. Allergy duplicate rule. "None reported" rule and contradiction flags. One banner, refreshed after every clinical write. Legacy entries kept and displayed. The add flow (5.3) only if OD-14 is approved. | OD-01..OD-05 |
| **A2** finalized encounters | Completion, adoption and diagnosis attachment refuse a Finalized encounter. Pickers exclude finalized encounters. Addendum unchanged. Signed drafts unchanged. | OD-06 |
| **A3** display and integrity | F-106, the F-105 wording guard, the Group B rows in the matrix, and the PD-09d display correction (2.1). | OD-07..OD-09 |

### 2.1 PD-09d: smallest safe non-semantic correction (subject to step 0)

No clinical rule is added or changed. The odontogram missing-tooth rule still covers Existing/Completed only. The change is to display only:
- Each tooth finding shows its recorded status, for example "Missing (whole tooth) - Diagnosed".
- The "absent" glyph is drawn only for an Existing/Completed missing finding.
- An implant and a missing finding are both listed as recorded. There is no superseding, no "replaced by" and no requirement either way.
- The stale tooth message clears (F-093), and an identical repeat says "already recorded" (F-094).

If step 0 shows that today's tile does not draw "absent" for Diagnosed/Planned, the glyph part is dropped and recorded.

## 3. Step 0: verification gate (read-only, per attempt)

Each item is recorded as CONFIRMED, NOT_REPRODUCED, DUPLICATE, WITHDRAWN or DESIGN_DECISION_REQUIRED, with file:line or dev-database evidence.

**Stop rule:** if any item is NOT_REPRODUCED, belongs to another package, or would need a new rule, schema, permission or API beyond this briefing, the attempt stops and returns a **briefing addendum** for review before building that item. The other items may continue only if they do not depend on it.

| # | Attempt | Check |
|---|---|---|
| 0.1 | A1 | Chart-name comparison: column collation (case), trailing-space behaviour. Count of live **allergy** items whose normalized names collide (read-only on dev DB). If any collide, stop: collisions are never merged. |
| 0.2 | A1 | The exact unique index behind `ClinicalWrite.IsUniqueViolation` on `ClinicalRecordItems` (columns, filter, status). |
| 0.3 | A1 | Does `section_has_items` count only active items? OD-04 refuses on **active** items. If the current rule is broader, narrowing it is in A1 and is tested. |
| 0.4 | A1 | Every reader of `EncounterEntries` and `EncounterSectionMarks`: encounter reader, list counts, finalize checks, export, client. |
| 0.5 | A1 | F-081 (two strips), F-086 ("not established" after encounter saves), Q-5f, Q-5g, Q-5i: reproduce in code paths. |
| 0.6 | A2 | Every completion, adoption and diagnosis path that sets or changes an encounter id. Q-5ad: the diagnosis picker offers finalized encounters. |
| 0.7 | A3 | F-106: which prescription fields exist (`PrescriptionWorkflow.Instructions`, `QuantityValue/Unit`, status, supersede link) and what the tab shows. No new field may be needed; if one is, stop. |
| 0.8 | A3 | F-105: every surface (server text, client components, print and export) that shows the allergy-check outcome. |
| 0.9 | A3 | F-067, F-079, F-082, F-089 (where the "patient" source default comes from), F-092, F-093, F-094, F-097, F-099, F-137, F-199, Q-5aa, Q-5ai, Q-5j: each located in code. |
| 0.10 | A3 | F-090 and F-098: whether the picker can store a reference without a schema change. If a column or foreign key is needed, stop for an addendum. |
| 0.11 | A3 | F-095: confirm that Plan/Complete are chart statuses only (explain-only is then enough). |
| 0.12 | A3 | F-091 display: what the tile draws today for each status. |
| 0.13 | A3 | Q-5aj: completion chart effect versus surface display. If PKG-08, stop for an addendum. |

## 4. A1 design

### 4.1 Write path (allergy, medication, medical history only)

The existing encounter routes keep their URLs and permissions:
- `POST/PUT api/encounters/{id}/entries`
- `.../entries/{entryId}/remove`
- `.../sections/{kind}/none-reported`
- `.../sections/{kind}/clear-review`

For kind Allergy, Medication or MedicalHistory:

| Encounter action | Becomes |
|---|---|
| Add entry | `ClinicalRecordService.AddItemAsync(..., Active, encounterId)` |
| Change entry | `UpdateItemAsync(..., encounterId)`, which appends a version |
| Remove entry | Withdraw as entered in error (reason required), allowed only for an item **added in this encounter**. Other items change by status, which appends a version. |
| "Reviewed - none reported" | Chart review `NoneKnown` with `encounterId`. Refused with 409 `section_has_active_items` while an active item exists. |
| "Confirm list is current" | Chart review `Reviewed` with `encounterId` |
| Clear review | Chart `NotReviewed` with `encounterId` |

**DentalHistory:** unchanged. It stays on `EncounterEntry` and `EncounterSectionMark` with today's rules and display (BR-3).

**Duplicates (BR-4):**
- **Allergies:** normalized name (trim, collapse inner whitespace, compare without case) equal to an **active** allergy gives 409 `duplicate_item` with the existing item's id, and the client links to it. Nothing is merged or changed. A retried identical add replays.
- **Medications and medical history:** today's chart duplicate behaviour, unchanged.
- **Display and search:** the stored name keeps the user's text, trimmed. Normalization is used only for the allergy comparison and for searching.

For the three moved kinds, legacy `EncounterEntry` and `EncounterSectionMark` rows are never written again. They are kept unchanged, with their tables and triggers.

### 4.2 Read path

- **What the encounter shows** for each of the three sections:
  - the chart's current items;
  - "recorded in this encounter" (versions with this `EncounterId`);
  - "reviewed in this encounter" (events with it);
  - for pre-A1 encounters, the legacy entries and marks, labelled as recorded before the chart was the single source.
- **Finalized encounters:** the provenance view is built only from append-only rows, so it cannot change. "The chart now" is shown separately.
- **Contradictions:** computed on read and never stored. A legacy "none reported" mark, or a chart `NoneKnown`, for a section that had an active item when it was made (from version history), or has one now. The flag reads "Contradiction: marked none reported while <n> active item(s) were listed". Both records are kept.

### 4.3 Banner

One `SafetyStrip` on the patient page shell; the encounter's second render is removed. One client helper, `notifySafetyChanged(patientId)`, is called after every successful clinical-record, alert or clearance write, and the strip refetches the server summary. A repo guard test refuses a clinical-record mutation call that bypasses the helper.

## 5. Migration and preservation plan

### 5.1 Schema

- **M1, indexes only:** on `ClinicalRecordItemVersions(EncounterId)` and `ClinicalRecordEvents(EncounterId)`; `Down` drops them.
- No data is written, moved or deleted, and no trigger is changed.
- No normalized-name column (0.1 stops on any collision).

### 5.2 Preservation

- **Rows kept unchanged:** every `EncounterEntry` (all kinds), `EncounterSectionMark`, `EncounterEvent`, `ClinicalRecordItem`, version and event row.
- **Proof:** row counts and checksums on the dev database before and after M1, which must be identical.
- **Export:** the "encounters" domain keeps `EncounterEntries`.
- **Rollback:** revert the code; chart rows written meanwhile are valid chart items with provenance under either version.

### 5.3 Legacy "add to chart" flow (OD-14 proposed; built only if approved; BR-5)

There is no automatic copy or backfill, ever. For a legacy allergy, medication or medical-history entry with no live chart item of the same kind and name, the encounter shows "Not on the chart" with an **Add to chart...** action. The action opens a form that:
- shows the historical text (name, reaction or severity, or dose and frequency, as recorded) and the encounter's date, read-only, labelled "as recorded on <date>";
- pre-fills the editable fields from it, with **no status pre-selected**: the clinician must choose the **current status** (Active, Inactive, Resolved or Discontinued as the kind allows) before it can be submitted;
- submits a **new** chart add through the normal chart route and rules, including the allergy duplicate rule;
- records provenance in the version: actor, time, reason "added from a legacy encounter entry", and the legacy entry's id in a link field.

Whether a link field exists without a schema change is a step-0 item. If it does not, the flow stops for an addendum before this flow is built. The legacy entry itself is never changed.

If OD-14 is not approved, legacy entries are display-only.

## 6. Provenance and audit plan

Each encounter-originated chart change writes, in **one save**:
1. the version (snapshot, `EncounterId`, actor, time, reason) or the review plus a `ClinicalRecordEvent` with `EncounterId`;
2. a PHI-free `EncounterEvent`;
3. the existing PHI-free `AuditService.Record` line.

Append-only triggers are unchanged. A test proves that an UPDATE or DELETE on versions and events is refused.

## 7. Authorization plan

| Action | Permission (unchanged) | Extra checks |
|---|---|---|
| Encounter section actions (A1) | ManageClinicalNotes | The encounter belongs to the item's patient; it is Draft (else `encounter_finalized`); it is not signed (existing lock); the patient is active |
| Reads | ViewClinicalDocumentation | - |
| Completion and adoption (A2) | ManageTreatmentPlans | The encounter is not Finalized and belongs to the plan's patient |
| Diagnosis create and re-link (A2) | ManageClinicalNotes | Same |

There are no new permissions or grants. Each route is tested for: 401, 403, another patient's encounter or item, and a finalized encounter.

## 8. A2 design

Completion, adoption, diagnosis create and every path that sets `EncounterId` load the encounter, and refuse with 409 `encounter_finalized` before any write when it is Finalized. Pickers exclude finalized encounters and say "Finalized encounters cannot receive new care. Start a new encounter." Signed drafts: today's behaviour, unchanged (OD-13 proposed).

Existing attachments made after `FinalizedAtUtc` are kept and labelled "attached after the encounter was finalized". The addendum route is unchanged.

## 9. Failure handling

- One transaction per write.
- Existing `RowVersion` concurrency and the unique-index race path are kept.
- The client shows the server's problem text and never retries a 409.
- No external calls.

## 10. Focused test plan (runs only after approval; each group of 5 minutes or less; anything longer is asked first)

| Attempt | Server (xUnit) | Client (vitest) | Browser |
|---|---|---|---|
| A1 | **Routing:** encounter add, change and remove reach the chart with `EncounterId` for Allergy, Medication and MedicalHistory; DentalHistory still writes `EncounterEntry` (boundary). **Allergy duplicate:** case, whitespace, active vs resolved (resolved: not refused by the new rule), retried replay, concurrent double add. **Category boundary (BR-4):** medication and medical-history duplicates behave exactly as before. **None-reported:** refused with an active item, allowed with only resolved items (per 0.3). **Contradiction derivation:** legacy mark and chart NoneKnown. **Provenance:** a finalized encounter is unchanged after a later chart change. Append-only refusal. **Auth matrix.** M1 up and down, with counts and checksums. About 3 min. | Sections render chart, provenance and legacy items; duplicate 409 links to the existing item; one banner; refetch after each write path; guard test. About 1 min. | One real-backend spec: allergy in an encounter, banner and Safety tab update without reload, duplicate refused. About 1-2 min. |
| A1 (only if OD-14 is approved) | The add flow refuses submission without a chosen status; it creates a new item; the legacy row is unchanged; provenance is linked. | No status pre-selected; historical text read-only. | - |
| A2 | Complete, adopt and diagnose: Draft accepted; Finalized refused with nothing written; another patient's encounter; auth. **No signed-draft test** (OD-13). About 1-2 min. | Pickers exclude finalized encounters; message. | - |
| A3 | Allergy-check verdict-word guard; prescription view fields; alert source not invented; tooth 99 field problem. About 1 min. | Each display row of the matrix, the PD-09d status rendering, pickers within their stated scope. About 1-2 min. | - |

**Negative controls (caught, then restored from a backup with a hash check):**
- A1: encounter-local duplicate check only;
- A1: `EncounterId` dropped from the version;
- A1: allergy rule applied to medications (the boundary test must catch it);
- A2: Draft check removed;
- A3: "safe" planted in the no-match text.

`fullRegression: DEFERRED_TO_ALV-N028`.

## 11. Out of scope

- The allergy-class list and any drug, interaction, dose, contraindication, compatibility or prescribing-decision logic, and any external drug API (deferred; a release limitation).
- Dental-history consolidation.
- New duplicate rules for non-allergy kinds.
- PD-09d clinical meaning.
- Connecting odontogram Plan/Complete.
- OD-10..OD-15 until decided.

## 12. Assumptions (logged)

1. Encounter removal is limited to items added in that encounter.
2. The stored name keeps the user's text, trimmed.
3. Contradiction flags are computed and never stored.
4. M1 adds indexes only, subject to 0.1.
5. The register's F-105 disposition stays `CONFIRMED`, with the deferral in its note (vocabulary unchanged).
