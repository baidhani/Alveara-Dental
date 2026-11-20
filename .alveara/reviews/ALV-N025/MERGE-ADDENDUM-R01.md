# ALV-N025 Merge-Integrity Addendum R01 — Independent Review

**Decision:** CHANGES_REQUIRED

**Reviewed package:** `Alveara_Briefing_ALV-N025_737b8be_MERGE-ADDENDUM-R01.zip`  
**Package SHA-256:** `929D0D0ACBA7E1EE4D44BF2AD450CF31291BA8F533BB3F57F829DE0746FEA15B`  
**Scope:** U-016/U-017 and merge-trigger integrity amendment only.

## Accepted design elements

- U-016 identifies the five missing merge-scope columns precisely.
- U-017 correctly explains why a document-linked clearance fails while the merge moves patient-owned tables, and preserves same-patient enforcement outside a merge and at end-of-merge verification.
- `AddMergeTriggerIntegrityRepairs` is correctly a fourth, forward-only migration. Editing the current trigger definitions with shape checks and exact-definition preservation proofs is appropriate for avoiding another stale trigger reconstruction.
- The named MRN-trigger exception is correct in principle: `51298` remains absolute, while ordinary merge triggers retain their stand-aside behavior.

## Required correction

### MA-01 — Prove the named MRN exception is absolute during a merge

The addendum names `TR_Patients_MrnImmutable` as the exception, and says `PatientMergeSchemaTests` will accept it, but it omits the required direct proof of the security/property boundary. Add a focused test that sets `alveara.patient_merge = 1`, attempts to change `Patients.Mrn`, and receives `51298`. Also assert the trigger definition does **not** contain the merge stand-aside condition. Retain the ordinary valid merge proof.

This prevents a future “make all triggers stand aside” change from silently making MRNs mutable during a merge.

## Resubmission boundary

Return a documents-only R02 that adds MA-01 to the test and control plan. Do not implement the fourth migration or its trigger changes until that addendum revision is approved.
