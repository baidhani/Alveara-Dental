# ALV-N022 pre-implementation briefing (revision 1): safety and clinical-record integrity, finalized encounters

- **Story:** ALV-N022 (execution item 59): R05 **PKG-06** and **PKG-07**.
- **Date:** 2026-11-16. **Session:** CC-20261115-m7x2. **Code read at:** `2eca1a9`.
- **Status:** **PROPOSED, awaiting independent briefing review.** This is a document only. No code, test, schema, migration, setting or permission has changed, and nothing was run.
- **Companion files:**
  - `docs/design/ALV-N022_OWNER_DECISION_RECORD.md`: decisions OD-01..OD-08, and the open and proposed items OD-09..OD-15.
  - `docs/design/ALV-N022_SCOPE_MATRIX.md`: every register row and where it goes.
  - `docs/design/ALV-N022_FINDING_SURVEY.md`: the read-only survey and the code evidence.
  - `docs/design/ALV-N022_DECISION_REQUEST.md`: the original request, superseded where the record differs.
- **Not authorized before approval:** implementation, migration, any drug or external API integration, or any broad or full-suite test run.

## 1. Problem in one paragraph

Allergies, medications and history exist twice: once on the patient's chart (`ClinicalRecordItem`) and once in each encounter (`EncounterEntry`).
- The safety banner, the Safety tab and the prescribing allergy check read only the chart.
- Encounter entries are checked for duplicates only within their own encounter, and never reach the chart.
- The banner refreshes only on an event that only the Safety tab sends.

This one design gap produces F-078, F-080, F-084, F-085, F-086, F-111 and F-112 (survey section 1). The chart side already has what the fix needs:
- `ClinicalRecordService.AddItemAsync` already takes an `encounterId`.
- Each change appends a `ClinicalRecordItemVersion`, and section changes append a `ClinicalRecordEvent`. Both carry `EncounterId`, and database triggers keep both append-only.
- A duplicate live item is refused (`duplicate_item`), race-guarded by a unique index.
- "None known" is refused when the section has items (`section_has_items`).

So the fix is mostly **routing**: the encounter's section actions call the chart service with the encounter's id. It is not a new data model.

## 2. Attempts

The attempts are sequenced. Each is one reviewed package. Nothing from a later attempt lands in an earlier one.

| Attempt | Content | Decisions |
|---|---|---|
| **A1** chart as single source | The encounter's allergy, medication, medical-history and dental-history actions write to the chart with encounter provenance. The encounter reads its sections from the chart. Duplicate, "none reported" and contradiction rules apply. One banner, refreshed after every clinical write. Legacy encounter entries are kept and shown, with the one-click "add to chart" option (OD-14, if approved). | OD-01..OD-05, OD-14 |
| **A2** finalized encounters | Completion, adoption and diagnosis attachment require a draft encounter. Pickers list only drafts. The addendum route is unchanged. Signed drafts follow OD-13 if approved. | OD-06, OD-13 |
| **A3** display and integrity corrections | F-106, the F-105 wording audit and guard, group B rows (scope matrix), and the PD-09d non-semantic display correction (2.1). | OD-07, OD-08 |

### 2.1 PD-09d: the smallest safe non-semantic correction

No clinical rule is added or changed: OdontogramService's missing-tooth rule still covers Existing/Completed only. What changes is **display of what is stored**:
- The tooth summary and tooltip show each finding with its recorded status, for example "Missing (whole tooth) - Diagnosed". The odontogram draws the "absent" glyph only for an Existing/Completed missing finding. A Diagnosed/Planned missing finding gets a status mark, not the absent glyph.
- Today the tile shows "missing" whatever the status. This is the cause of F-091's "impossible combination" reading.
- An implant and a missing finding on the same tooth are both listed as recorded (Q-5v). There is no "replaced by" text, no superseding and no requirement either way (Q-5w).
- Messages: the stale tooth message clears when the selected tooth changes (F-093). An identical repeat says "Already recorded; nothing was added" (F-094).

## 3. Step 0 (read-only, at the start of each attempt)

R05 asks for verification first. These are read-only checks of code and the dev database, recorded in the evidence:

1. **Name comparison.** The chart's duplicate check compares `Name == name` in SQL. Confirm the column collation is case-insensitive, and that trailing spaces compare equal (SQL Server padding). Report how many live chart items have leading or inner repeated whitespace (read-only count on the dev database).
2. **Unique index.** Confirm the exact index behind `ClinicalWrite.IsUniqueViolation` on `ClinicalRecordItems`: its columns, its filter, and whether it includes status.
3. **"None known" rule.** Confirm whether `section_has_items` counts inactive or resolved items. OD-04 speaks of active items, so if it counts every live item, A1 narrows it to active items only, and a test proves that a resolved allergy plus "none known" is allowed.
4. **Encounter readers.** List every reader of `EncounterEntries` and `EncounterSectionMarks`: the encounter reader, the list counts (F-082), finalize checks, export (`ExportDomainCatalog` "encounters"), and client pages.
5. **A2 paths.** List every completion, adoption and diagnosis path that takes or changes an encounter id.
6. **Pending register rows.** Re-read the register-only rows of the survey in code (F-081, F-086, F-089, F-067...), and Q-5aj.

If step 0 contradicts this briefing, I stop and report before building.

## 4. A1 design

### 4.1 Write path

The existing encounter routes keep their URLs and permissions:
- `POST/PUT api/encounters/{id}/entries`
- `POST .../entries/{entryId}/remove`
- `POST .../sections/{kind}/none-reported`
- `POST .../sections/{kind}/clear-review`

Their behaviour changes:

| Encounter action | Becomes |
|---|---|
| Add entry | `ClinicalRecordService.AddItemAsync(patient, kind, fields, Active, encounterId)`: the chart item, with version 1 carrying `EncounterId` |
| Change entry | `UpdateItemAsync(..., encounterId)` on the chart item, which appends a version |
| Remove entry | Withdraw as entered in error (`RemovedInError`, reason required) on the chart item. Only an item **added in this encounter** may be removed from the encounter. Others are changed by status (Resolved, Discontinued), which appends a version. |
| "Reviewed - none reported" | Chart section review `NoneKnown` with `encounterId`. Refused with 409 `section_has_active_items` while an active item exists (OD-04). |
| "Confirm list is current" (new in the encounter, Q-5i) | Chart section review `Reviewed` with `encounterId` |
| Clear review | Chart `NotReviewed` with `encounterId` |

Name normalization (OD-03) is one function used on every chart write: trim, collapse inner whitespace, then a case-insensitive compare. The stored name is the normalized text with the user's case kept. A retried identical add replays the existing item (the current idempotent behaviour). A different add with the same normalized name is refused with 409 `duplicate_item`, with the existing item's id in the problem detail so the client can link to it. Nothing is merged.

Legacy `EncounterEntry` and `EncounterSectionMark` rows are **never written again**: the service no longer inserts or changes them. Their tables, triggers and rows stay as they are.

### 4.2 Read path

- **What the encounter shows, per section:**
  - the chart's current items;
  - "recorded in this encounter": the versions whose `EncounterId` is this encounter;
  - "reviewed in this encounter": the section events carrying it;
  - for an encounter that started before A1, its legacy entries and marks, labelled "recorded in this encounter (before the chart was the single source)".
- **Section addressed** (the encounter's completeness signal): the section was changed or reviewed during this encounter, or it holds legacy entries or a legacy mark. Finalizing keeps today's rule on unaddressed sections.
- **Finalized encounters:** their provenance view is built only from append-only rows (versions and events with that encounter id, and the legacy rows), so it cannot change after finalization. The chart's current items are shown separately as "the chart now", never as what the encounter recorded.
- **Contradictions (OD-04), computed on read and never stored:**
  - a legacy encounter "none reported" mark, or a chart `NoneKnown` review, for a section that had an active item at the time the mark was made (taken from the item versions' history);
  - or a chart `NoneKnown` that coexists with an active item now.

  Each is shown as "Contradiction: marked none reported while <n> active item(s) were listed". Neither record is changed.

### 4.3 Banner (OD-05)

- **One render.** One `SafetyStrip` on the patient page shell; the second render inside the encounter is removed (F-081).
- **Refresh.** One client helper, `notifySafetyChanged(patientId)`, is called after every successful clinical-record write: chart, encounter section actions, alerts and clearances. `SafetyStrip` refetches the server summary when it hears it. The server stays the only source of what the banner says.
- **Guard test.** A repo check (`tests/`) fails if a module calls a clinical-record mutation API without going through the helper.

## 5. Migration and preservation plan

- **Schema (M1, small, reversible):**
  - add an index on `ClinicalRecordItemVersions(EncounterId)` and one on `ClinicalRecordEvents(EncounterId)`, for the per-encounter read;
  - nothing else in A1;
  - `Down` drops them;
  - no data is rewritten, moved or deleted;
  - no trigger is changed.
- **Normalized-name column:** not added unless step 0 item 1 finds live names whose normalized forms collide. If it does, I stop and bring options to the owner; collisions are never merged by a migration.
- **Legacy entries (OD-14), proposed option M-A:**
  - no automatic copy into the chart, because a copy would create chart facts nobody confirmed;
  - for each legacy entry with no live chart item of the same kind and normalized name, the encounter shows "Not on the chart: Add to chart";
  - a clinician with ManageClinicalNotes can then add it in one click, as a normal chart add with provenance `EncounterId` = the legacy encounter and reason "added from a pre-A1 encounter entry".
  - **Alternative M-B:** a one-time backfill that creates Active chart items from every legacy entry. **Not recommended**, because it silently asserts current facts.
- **Data the migration must preserve:** every `EncounterEntry`, `EncounterSectionMark`, `EncounterEvent`, `ClinicalRecordItem`, version and event row, unchanged. The proof is a row count and checksum of these tables on the dev database before and after M1, with identical results.
- **Export:** the "encounters" export domain keeps `EncounterEntries`, so historical data still exports. Chart items already export with the clinical record.
- **Rollback:** reverting A1's code restores the old write path. The new chart rows written meanwhile are ordinary chart items with provenance, valid under either version. M1's `Down` drops only the indexes.

## 6. Provenance and audit plan

Every encounter-originated chart change writes three things in **one save**, so they stand or fall together:
1. the `ClinicalRecordItemVersion` (full snapshot, `EncounterId`, actor, time, reason when given), or for a review the `ClinicalSectionReview` change plus a `ClinicalRecordEvent` carrying `EncounterId`;
2. an `EncounterEvent`, PHI-free, for example "Allergy added to the chart in this encounter";
3. the existing `AuditService.Record` line, PHI-free.

No clinical text goes into events or audit detail (the existing rule). Version and event tables stay append-only (existing triggers). A test proves that an UPDATE or DELETE is refused.

## 7. Authorization plan

| Action | Permission | Extra server checks |
|---|---|---|
| Encounter section actions (A1) | ManageClinicalNotes (unchanged) | The encounter exists and belongs to the item's patient. It is a Draft, otherwise `encounter_finalized`. It is not signed (the existing lock). The patient is active. The chart item belongs to the same patient. |
| Read the encounter, chart or banner | ViewClinicalDocumentation (unchanged) | - |
| Completion and adoption (A2) | ManageTreatmentPlans (unchanged) | The encounter is a Draft and belongs to the plan's patient. Signed drafts follow OD-13. |
| Diagnosis create and relink (A2) | ManageClinicalNotes (unchanged) | Same Draft and patient check |

There are no new permissions or grants. Each route gets tests for: 401 when unauthenticated, 403 without the permission, refusal of another patient's encounter or item, and refusal of a finalized encounter.

## 8. A2 design (summary)

`CompletionService` (complete and adopt) and `DiagnosisService` (create, and every path that sets `EncounterId`) load the encounter. They refuse with 409 `encounter_finalized` before any write when it is not a Draft. The client pickers request drafts only, and say "Finalized encounters cannot receive new care. Start a new encounter." when there are none.

Existing completions or diagnoses attached to a finalized encounter after its `FinalizedAtUtc` are kept. They are shown with "attached after the encounter was finalized" (display only). The addendum route and its triggers are unchanged.

## 9. Failure handling

- Every write is one transaction: a refused or failed save leaves nothing behind.
- The existing optimistic concurrency (`RowVersion`) and the unique-index race path stay in place.
- The client shows the server's problem text and never retries a 409.
- No external calls are added.

## 10. Focused test plan

Per attempt, run only the named tests. The estimates follow measured durations (`reference_measured_durations`). Any group estimated above 5 minutes is put to the owner before it runs, and nothing broad is run.

| Attempt | Server (xUnit, template-clone DB) | Client (vitest) | Browser |
|---|---|---|---|
| A1 | Encounter add/change/remove goes to the chart with `EncounterId`, for every kind. Duplicate: case, whitespace, active vs resolved, retried identical add replays, concurrent double add (one wins, one refused or replayed). None-reported refused while an active item exists, allowed with only resolved items. Contradiction derivation (legacy mark, chart NoneKnown). Finalized-encounter provenance unchanged after a later chart change. Version and event append-only. Auth matrix (401/403/other patient/finalized/signed). M1 up/down; row counts and checksums preserved. About 3 min. | Encounter sections render chart plus provenance plus legacy; duplicate 409 links to the existing item; one banner; banner refetches after each write path; guard test. About 1 min. | One real-backend spec: add Penicillin in an encounter, see the banner and Safety tab update without reload, then a duplicate refused. About 1-2 min. |
| A2 | Complete, adopt and diagnose against Draft (accepted), Finalized (409, nothing written), signed draft (OD-13), other patient's encounter, and auth. About 1-2 min. | Pickers list drafts only; refusal message. Under 1 min. | none |
| A3 | Allergy-check text guard (no verdict words), prescription view fields, alert source not invented, tooth 99 field problem. About 1 min. | Each display row in the scope matrix, the PD-09d status rendering, the pickers (F-090, F-098). About 1-2 min. | none |

**Negative controls (each caught, then restored from a backup with a hash check):**
- A1: re-enable the encounter-local duplicate check only.
- A1: drop `EncounterId` from the version.
- A2: remove the Draft check.
- A3: plant "safe" in the no-match text.

**Repo checks:** `node --test tests/*.test.mjs`, `tsc`, lint.

`fullRegression: DEFERRED_TO_ALV-N028`, as for ALV-N021.

## 11. Out of scope (restated)

- Any allergy-class list.
- Drug, interaction, dose or contraindication logic.
- Any external drug API.
- The clinical meaning of Diagnosed/Planned missing teeth, extraction intent or implant relationships (OD-09, open).
- Connecting odontogram Plan/Complete to treatment plans (F-095 is explained, not connected).
- Proposed items OD-10..OD-13 and OD-15 until the owner decides.

## 12. Assumptions (logged)

1. The dental-history section moves to the chart with the other three, because it shares the model and the encounter routes. Leaving it behind would keep two sources.
2. Removing from the encounter is limited to items added in that encounter. Other items are changed by status. This avoids an encounter erasing a chart fact it did not record.
3. Normalization keeps the user's case in the stored name.
4. The contradiction flag is computed and never stored.
5. M1 adds indexes only, subject to step 0 item 1.
