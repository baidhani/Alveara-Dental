# ALV-N022 A2: Step 0 verification record and briefing addendum 01

**Story:** ALV-N022 (plan item 59), attempt A2: finalized encounters cannot receive new care (briefing revision 3, section 8; OD-13 proposed, not decided).
**Status:** documents only. Nothing built. A1 is COMPLETE (review `.alveara/reviews/ALV-N022/A1-R02.md`, closure `2690d7a`). Per that review, A2 may begin only under its separately approved scope; this record asks for that approval.
**Method:** read-only code search on `2690d7a`, and read-only `SELECT` counts on the dev database `AlveraDev` (no write; disclosed below).

## 1. Step 0 check 0.6: every path that sets or changes an encounter link

Briefing section 8: "Completion, adoption, diagnosis create and **every path that sets `EncounterId`** load the encounter, and refuse with 409 `encounter_finalized` before any write when it is Finalized."

| # | Path (server) | Sets the link | Encounter check today | Client picker and its source | Result |
|---|---|---|---|---|---|
| P1 | Diagnosis create - `DiagnosisService.cs:45-54` | on create | patient match only (`encounter_not_found` 404) | `DiagnosisForm.tsx:141` from `listEncounters` (all statuses, status shown) | CONFIRMED |
| P2 | Complete procedures - `CompletedProcedureService.Context.cs:19-20`, row at `CompletedProcedureService.cs:121` | on create | patient match only (field error) | `TreatmentPlanReviewComplete.tsx:128` from readiness `CompletedProcedureService.Readiness.cs:41` (latest 50, all statuses) | CONFIRMED |
| P3 | Adopt a completion - `CompletedProcedureService.Adopt.cs:66-67, 104` | on create | patient match only | `TreatmentPlanAdoptCompletion.tsx:139` from the same readiness list | CONFIRMED |
| P4 | Amend a completion - `CompletedProcedureService.Amend.cs:39, 58-59` | changes the link | patient match only | none: no client code calls `amendCompletedProcedure` (API only) | CONFIRMED (a change path: see question Q-2) |
| P5 | Document import - `DocumentImportService.cs:47, 69` via `DocumentMetadataChecker.cs:49-53` | on create | patient match only | `DocumentContextFields.tsx` from `DocumentLibraryService.cs:30` (all statuses; the same list feeds the library FILTER) | **DESIGN_DECISION_REQUIRED** (Q-1) |
| P6 | Document edit - `DocumentEditService.cs:23-37` | changes the link | patient match only | `DocumentEditForm.tsx` (same list) | **DESIGN_DECISION_REQUIRED** (Q-1, Q-2) |
| P7 | Referral case create / update - `ExternalCaseService.cs:39-44, 98-102, 178-181` | on create; on update only when the link changes | patient match only (`encounter_invalid` 400) | none: `NewCaseForm.tsx:48` always sends `encounterId: null`; `CaseEditForm.tsx:37` sends back the existing value (API only) | **DESIGN_DECISION_REQUIRED** (Q-1, Q-2) |
| - | Lab cases | no encounter column (`INFORMATION_SCHEMA`) | - | - | NOT APPLICABLE |
| - | A1 chart writes with `encounterId` | already refused when not open (`encounter_not_open`, A1) | - | - | DONE IN A1 |

**Q-5ad** (the diagnosis picker offers finalized encounters): **CONFIRMED** - `DiagnosisForm.tsx:141` lists every encounter from `listEncounters`, labelled with its status. The completion and adoption pickers do the same (P2, P3).

**No path checks the encounter's status today**: every one only checks that the encounter belongs to the patient.

## 2. Existing data ("attached after the encounter was finalized")

Read-only counts on `AlveraDev` (4 encounters, 2 finalized):

| Kind linked to a finalized encounter and recorded after its `FinalizedAtUtc` | Count | Time used |
|---|---|---|
| Diagnoses | 2 | `Diagnoses.CreatedAtUtc` |
| Completed procedures | 2 | `CompletedProcedures.FinalizedAtUtc` (the completion record's own time) |
| Documents | 0 | `Documents.ImportedAtUtc` |
| Referral cases | 0 | `ReferralCases.CreatedAtUtc` |

So the label in briefing section 8 applies to real rows. For a link made by **creating** a row, the creation time decides it. For a link **changed later** (P4, P6, P7), the row's create time is not the link time: completion amendments keep their own rows; documents have `DocumentEvents`; referral cases keep only `UpdatedAtUtc` (not link-specific). See Q-3.

## 3. Stop conditions met (briefing section 3)

- P5-P7 are inside "every path that sets `EncounterId`" but the briefing's design and test plan name only completion, adoption and diagnosis, and refusing them is a **new rule** for the documents and referral areas (another package's surface): stop for Q-1.
- The label needs a link time that two change paths do not record precisely: stop for Q-3.

P1-P3 do not depend on either and can proceed once A2 is authorized.

## 4. Questions for the reviewer (addendum 01)

- **Q-1. Documents and referral cases.** Recommended: **A** - leave document import/edit and referral cases as they are in A2 (a result, X-ray or letter that arrives after the visit is legitimately filed against it; they are records about the visit, not new care) and record them in the register for a later decision. Alternative **B**: refuse them too with `encounter_finalized` and filter their pickers (the document library FILTER keeps every encounter either way).
- **Q-2. Change paths.** Recommended: refuse only when the link is being **set to a different** finalized encounter; editing other fields of a row that already points to a finalized encounter stays allowed (amending a completion's provider or time is a correction, not new care). Applies to P4 (and P6/P7 if Q-1 = B).
- **Q-3. The label.** Recommended: label rows created after the encounter's `FinalizedAtUtc` (diagnoses by `CreatedAtUtc`, completions by their record time, and documents/referrals by create time if Q-1 = B); a link changed later on an amendment row uses that amendment's time. No new column, no migration.
- **Q-4. Signed drafts.** Unchanged (OD-13 proposed, not decided): A2 refuses only `Finalized`; a signed draft behaves as today and has no test, as the briefing says.

## 5. A2 plan if approved (unchanged from briefing sections 8 and 10 except as the answers say)

- Server: P1-P4 load the encounter and refuse 409 `encounter_finalized` before any write when it is Finalized (P4 per Q-2); readiness and diagnosis picker lists exclude finalized encounters; the label per Q-3 on the diagnosis and completion views.
- Client: the diagnosis, complete and adopt pickers (the only client pickers among P1-P4) exclude finalized encounters and show "Finalized encounters cannot receive new care. Start a new encounter."; the label is shown. P4 and P7 have no client picker (API only).
- Tests (about 1-2 min server, about 1 min client): Draft accepted; Finalized refused with nothing written (per path); another patient's encounter; auth; picker exclusion; the label. No signed-draft test (OD-13).
- Negative control from the briefing: the Draft/Finalized check removed. Plus one per path.
- `fullRegression: DEFERRED_TO_ALV-N028`.

## 6. Disclosure
Read-only `SELECT`s on `AlveraDev` (`Encounters`, `Diagnoses`, `CompletedProcedures`, `Documents`, `ReferralCases`, `INFORMATION_SCHEMA.COLUMNS`); nothing was written.
