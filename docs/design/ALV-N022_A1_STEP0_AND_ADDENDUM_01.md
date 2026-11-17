# ALV-N022 A1 - Step 0 verification and briefing addendum 01

- **Date:** 2026-11-16. **Session:** CC-20261115-m7x2. **Code at:** `a995798`. **Authority:** `.alveara/reviews/ALV-N022/BRIEFING-R03.md` (A1 only).
- **What was done:** read-only code reading, plus read-only `SELECT`s on the dev database `AlveraDev`. No write, no schema change, no test run. No product code changed.
- **Outcome:** checks 0.1-0.5 are recorded below. **One stop condition** (S-1) is raised for medication and medical-history duplicates on the encounter path, so A1 stops for this addendum. Allergy routing and the banner work do not depend on S-1.

## 1. Step 0 results

| # | Result | Evidence |
|---|---|---|
| 0.1 name comparison | CONFIRMED with detail. `Name` is `SQL_Latin1_General_CP1_CI_AS` (case-insensitive; trailing spaces compare equal under SQL padding). **Inner repeated whitespace is not normalized** by SQL. Dev DB: 1 live allergy, 1 medication, 1 medical-history item; 0 names with odd spacing; **0 normalized active-allergy collisions** (small sample, disclosed). No stop. | sys.columns, sys.indexes and counts on AlveraDev (SELECT only) |
| 0.2 unique index | CONFIRMED: `IX_ClinicalRecordItems_PatientId_Kind_Name`, unique on (PatientId, Kind, Name), filter `RemovedAtUtc IS NULL`, **all statuses**. So the chart already refuses a second live item of the same kind and name (case-insensitive) even when the first is Resolved or Discontinued, with `duplicate_item` "Change that item instead". This is the existing rule for **every** kind. | sys.indexes; `ClinicalRecordService.cs` 43-44, 66-71, 232-236 |
| 0.3 "none known" rule | CONFIRMED broader than OD-04: `SetReviewAsync` refuses NoneKnown/Unknown when **any live** item exists (`ClinicalRecordService.cs` 158-159). As the briefing (0.3) allows, A1 narrows **NoneKnown only** to active items. Unknown and Reviewed are unchanged. | code |
| 0.4 readers | CONFIRMED. `EncounterEntries`/`EncounterSectionMarks` are read by `EncounterReader.cs` 16-17 (detail) and 53-54 (list counts, F-082), `EncounterService.cs` (writes, duplicate and mark checks), and `ExportDomainCatalog.cs` 29 (export, kept). Finalize completeness uses `EncounterReader` `IsComplete`/`MissingSections` (`EncounterService.cs` 209-213), so A1 must derive these from the chart provenance for the three moved kinds. Client: `EncounterView.tsx`, `SectionPanel.tsx`, `services/clinicalApi.ts`. | code |
| 0.5 findings | **F-081 CONFIRMED**: two strips (`PatientHeader.tsx` 64 and `EncounterView.tsx` 146). **F-078 CONFIRMED and wider**: `announceSafetyChanged` is sent only by `SafetyPanel.tsx` 107/126, so chart-page saves (`ClinicalRecordPanel`) do not refresh the banner either; OD-05 covers both. **F-086 CONFIRMED**: encounter "none reported" writes `EncounterSectionMarks`, never a chart review. **Q-5f, Q-5g, Q-5i CONFIRMED**: the encounter reads only its own entries (`EncounterReader.cs` 16). | code |

**Also found (reuse, no stop):** the chart already guards the encounter link. `RequireOpenEncounterAsync` (`ClinicalRecordService.cs` 202-210) refuses another patient's encounter (`encounter_patient_mismatch`), and a finalized **or signed** encounter (`encounter_not_open`). A1 reuses these codes instead of the `encounter_finalized` named in briefing section 7. Signed-draft behaviour for chart writes is therefore today's behaviour (already refused), so this does not touch OD-13.

## 2. Stop S-1: medication and medical-history duplicates on the encounter path

**Fact:**
- Today an encounter may record "Ibuprofen" even when the chart already lists Ibuprofen, because the encounter checks duplicates only within itself.
- Once encounter actions write to the chart (OD-01), that add meets the chart's **existing** unique rule (0.2), and the database itself refuses it.
- So for medications and medical history, the **chart's** duplicate semantics stay unchanged (BR-4), but what a user sees **in the encounter** changes. Re-recording an item already on the chart becomes a refusal instead of a second entry.
- BR-4 asked that legitimate repeated entries not be rejected unexpectedly.

**Options:**

| Option | Behaviour in the encounter for a medication/medical-history name already live on the chart | Schema |
|---|---|---|
| **A (recommended)** | **Not a refusal to the user.** The encounter offers the existing chart item: "Already on the chart: confirm it is current, or update it". Confirming records a chart section review (`Reviewed`) with this encounter's provenance. Updating appends a version with provenance. No second item is created. The API add returns 409 `duplicate_item` with the existing item's id, the same as the chart page today. | none |
| B | Allow a second live item of the same name for medications and medical history (for example a dose change recorded as a new item). | Changes the unique index: a new rule and a migration beyond the briefing. Not recommended. |
| C | Keep medications and medical history on the encounter-entry model. | Contradicts OD-01 (single source); F-085 and F-111 stay open. |

**Allergies:**
- An allergy whose normalized name matches a live allergy is refused today by the same index, active or resolved. OD-03's "active duplicate refused, directed to the existing item" is met. A resolved match is also refused, by the existing rule, and directed to the existing item to change its status. Nothing is merged.
- Inner whitespace: so the database index also catches "Penicillin  V" against "Penicillin V" under concurrency, A1 **stores allergy names with inner whitespace collapsed** (the user's case is kept). This replaces briefing assumption 2 ("trimmed") for allergies only.

## 3. Requested

Approval of **S-1 option A** (no schema change) and of the two reuse points (the existing error codes, and the collapsed allergy whitespace). Until then, A1 builds only what does not depend on S-1:
- allergy routing;
- the NoneKnown narrowing;
- banner (OD-05);
- M1 indexes.

Or A1 waits as a whole, if the owner prefers.

## 4. Addendum 02 (2026-11-16, answering `.alveara/reviews/ALV-N022/A1-STEP0-ADDENDUM-01.md`)

**Accepted by review:** S-1 option A; reuse of `encounter_not_open` / `encounter_patient_mismatch` (no signed-draft policy change); canonicalization of **new** allergy input (trim, collapse inner whitespace, keep case; no historical row rewritten or merged); and the condition that a real deployment database gets the same read-only normalized active-allergy collision check before A1's migration, a collision stopping that work for review.

**A1-01 correction.** Section 2's statement that the existing refusal of a resolved match "meets OD-03" was inaccurate: OD-03 as written named only the active case. The user-visible outcomes are now precise, one record is kept in every case, and no index or clinical rule changes:
1. **Active match:** nothing added; the user is directed to update the active item.
2. **Resolved or Inactive match** (allergies have no Discontinued status): nothing added; an explicit **reactivate/update the existing record** action is offered; the user must choose the new status (none pre-selected); nothing changes until it is submitted, which appends one version with provenance.
3. **Cancel:** every existing item unchanged.
4. **Concurrency:** two simultaneous adds leave one record (existing unique index); the losing request gets the same existing-record direction.

Updated in this addendum: decision record OD-03 acceptance criterion (agent wording column; the owner's words are unchanged), briefing 4.1, 5.1 and 10, scope matrix F-084. Documents only; no product code, schema, test or configuration changed; no test run.

## 5. Step 3 finding (2026-11-17): migration M1 is not needed

The two indexes the briefing planned for M1 already exist, created by EF Core with the foreign keys: `IX_ClinicalRecordItemVersions_EncounterId` and `IX_ClinicalRecordEvents_EncounterId` (read-only `sys.indexes` check on AlveraDev; also in the model snapshot by convention). A1 therefore adds **no migration and no schema change**. `dotnet ef migrations has-pending-model-changes` reports no changes. The preservation proof (row counts and checksums before and after M1) becomes a proof that A1 changes no schema; the deployment collision preflight condition still applies before A1 is deployed.
