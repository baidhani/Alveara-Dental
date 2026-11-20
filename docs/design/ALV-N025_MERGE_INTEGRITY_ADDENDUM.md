# ALV-N025 — Merge-integrity addendum (U-016, U-017; for independent review)

**Status:** proposed, documents only. **No code for the fourth migration before an independent review approves it** (owner decision, 2026-11-19).
**Amends:** `ALV-N025_PRE_IMPLEMENTATION_BRIEF.md`, revision 3 (approved in BRIEFING-R03), and the three-migration packaging recorded in its section 6 (owner decision MIG-1). This adds a **fourth forward migration** to ALV-N025.
**Owner decision (2026-11-19, quoted):** "U-016 and U-017 are merge-integrity regressions, the same family as U-015, and leaving PatientMergeSchemaTests knowingly red would undermine this story's merge work." The full findings are in `docs/stabilization/UNASSIGNED_FINDINGS.md` (U-016, U-017), now assigned to ALV-N025.

## 1. How they were found

During slice 3's focused run, `PatientMergeSchemaTests` failed two schema-wide rules:

1. Every table that holds a patient id has a merge-scope trigger listing every other column, so that during a merge only patient ids may change (51234).
2. Every update trigger on those tables stands aside during a merge (session value `alveara.patient_merge = 1`).

A read-only query on the dev database at `20261119034014` (the ALV-N024 schema, before any ALV-N025 schema change) showed the same gaps. They are therefore not caused by ALV-N025. `PatientMergeSchemaTests` was not in the focused groups of ALV-N022 A3 or ALV-N024.

The MRN guard `TR_Patients_MrnImmutable` was also named by rule 2. That is handled separately, as a named exception by owner decision (51298 stays absolute), and is **not** part of this addendum.

## 2. U-016: the five columns missing from their merge-scope triggers

| Table | Trigger | Missing column | Added by |
|---|---|---|---|
| `Clearances` | `TR_Clearances_MergeScope` | `DocumentId` | ALV-N022 A3, `20261117180509_AddTypedClearanceDocumentAndPerioDiagnosisLinks` |
| `ClearanceVersions` | `TR_ClearanceVersions_MergeScope` | `DocumentId` | same |
| `PerioExamLinks` | `TR_PerioExamLinks_MergeScope` | `DiagnosisId` | same |
| `FinancialDocuments` | `TR_FinancialDocuments_MergeScope` | `ReplacementReason` | ALV-N024, `20261119034014_AddReceiptReplacementAndPayerEligibility` |
| `FinancialDocuments` | `TR_FinancialDocuments_MergeScope` | `ReplacesDocumentId` | same |

**Effect:** during a merge, a change to any of these five columns is not refused, unlike every other column of those tables.

**Correction:** add `UPDATE([<column>]) OR ` to each trigger's existing chain of `UPDATE([...])` terms.

## 3. U-017: `TR_Clearances_DocumentSamePatient` does not stand aside

The trigger (ALV-N022 A3, error 51288) runs `AFTER INSERT, UPDATE` on `Clearances` and refuses a clearance whose `DocumentId` points at a document of another patient.

**Why a merge fails:**
- The merge moves patient-owned tables one statement at a time, in the order of `PatientOwnedData.All`, which is alphabetical. So `Clearances` moves **before** `Documents`.
- When a clearance with a linked document moves to the survivor, its document still belongs to the retired patient until the next statement. The trigger refuses that with 51288, and the whole merge rolls back.
- An undo-merge (unmerge) moves rows back under the same session value and in the same order, so it would fail the same way.

**Expected effect, to be confirmed first:** a patient who has a clearance linked to a document cannot be merged. The fix is written only after the reproduction test (section 5) shows this failure on the current schema.

**Correction:**
- Add the repository's standard stand-aside line as the trigger's first statement after `SET NOCOUNT ON;`, exactly as the AddPatientMerges migration gives the other triggers:
  `IF CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')) = 1 AND ((EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)) OR NOT (EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted))) RETURN;`
- **It stands aside only for an UPDATE made inside a merge.** An INSERT is still checked, and so is any update outside a merge.
- **Same-patient consistency is still enforced at the end of every merge.** `PatientMergeService.VerifyAsync` checks each foreign-key edge of the merge catalogue: no child row may disagree with its parent row about whose chart it is. `Clearances → Documents` is such an edge (`FK_Clearances_Documents_DocumentId`), so a merge that left a clearance and its document on different patients would still be refused (`merge_inconsistent`), and nothing would be merged.

## 4. The fourth forward migration: `AddMergeTriggerIntegrityRepairs`

**Up** (one transaction):
- **U-016:** for each of the four triggers in section 2, a **text edit of the trigger as it is now**:
  - read its current definition from `sys.sql_modules`;
  - insert the missing `UPDATE([<column>]) OR ` term(s) before the first `UPDATE([`;
  - turn `CREATE` into `ALTER` and execute it.
  - It is never re-created from a template, which is how U-015 lost a guard. It is the same technique as the U-015 restore migration and `AddPatientMrn`'s `UPDATE([Mrn])` term.
  - Each edit is skipped if the term is already there.
  - It refuses (`THROW 50000`) if the trigger is missing or has a shape it does not know.
- **U-017:** the same kind of text edit puts the stand-aside line after `SET NOCOUNT ON;` in `TR_Clearances_DocumentSamePatient`. It is skipped if the line is already there, and refuses if `SET NOCOUNT ON;` is not found.
- No data is read or written.

**Down:** removes exactly the inserted terms and the inserted line by the same kind of text edit, giving back the definitions as they were. No data, so nothing can be lost; no refusal guard is needed.

**Order:** after `AddPatientMrn`, before slices 4 and 5's migrations (`AddPatientCountry`, `AddPatientAccessAudit`). `TR_Patients_MergeScope` is not touched here; `AddPatientMrn` already handles its `Mrn` term.

## 5. Tests (focused; `MergeTriggerIntegrityTests`, plus `PatientMergeSchemaTests`)

1. **Reproduction first (U-017):** on the schema just before the new migration, merging a patient whose clearance links a document fails with 51288. Recorded as the pre-fix evidence.
2. **The merge works after the repair:** the same merge succeeds; the clearance and its document both belong to the survivor; `VerifyAsync` finds no disagreement. The undo of that merge also succeeds, and both rows go back to the restored patient.
3. **U-017's current rule is preserved:**
   - outside a merge, an UPDATE or INSERT pointing a clearance at another patient's document is still refused with 51288;
   - inside a merge session, an INSERT is still refused (the stand-aside covers only updates).
4. **U-016, direct SQL, for each of the five columns:** inside a merge session, an UPDATE of the column is refused with 51234. Outside a merge, the column behaves exactly as before the repair: the same statement gives the same outcome before and after.
5. **Every other rule of each repaired trigger is preserved:** after Up, each repaired trigger's definition equals its pre-migration definition plus exactly the inserted text, with nothing else changed (string comparison of the definitions read before and after). After Down, each definition equals the pre-migration definition exactly.
6. **`PatientMergeSchemaTests`** passes in full: both schema-wide rules, with the one named MRN exception decided by the owner.
7. **Controls:**
   - with the U-017 line removed, test 2 must fail;
   - with one U-016 term removed, its test in 4 and the schema rule must fail.

Focused groups: `MergeTriggerIntegrityTests`, `PatientMergeSchemaTests`, `PatientUnmergeSchemaTests`, `PatientMergeServiceTests`, `RetiredRecordGuardTests`.

## 6. Disclosure

- **ALV-N025 now has four forward migrations:** `AddPatientMrn`, `AddMergeTriggerIntegrityRepairs`, `AddPatientCountry`, `AddPatientAccessAudit`.
- **The fourth repairs regressions from two closed stories,** ALV-N022 A3 (four of the gaps: three missing columns and the stand-aside) and ALV-N024 (two columns), found by ALV-N025's focused runs.
- **It is disclosed in the ALV-N025 handoff together with U-015.** The handoff states that `PatientMergeSchemaTests` had been red since ALV-N022 A3 without being run.

## 7. Assumptions (logged)

1. The merge's catalogue includes the `Clearances → Documents` edge (it derives edges from the model's foreign keys between owned tables), so `VerifyAsync` covers the consistency the stand-aside gives up mid-merge. The first test will confirm this before any reliance on it.
2. No other trigger on a patient-owned table has the same mid-merge ordering problem. The schema rule (section 1, rule 2) lists every update trigger without the stand-aside line, and after this repair it lists only the named MRN exception.
