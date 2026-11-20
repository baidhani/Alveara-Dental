# ALV-N025 Merge-Integrity Addendum R02 — Independent Review

**Decision:** APPROVED

**Reviewed package:** `Alveara_Briefing_ALV-N025_cf8ad05_MERGE-ADDENDUM-R02.zip`  
**Package SHA-256:** `4B2DBB0812424A5CFC86ADA5334C7FA378032A3C9264F12FEEEC7CC9A563BAAB`  
**Scope:** U-016/U-017 merge-integrity amendment and MA-01 only.

## Findings

MA-01 is resolved. The addendum now requires and the supplied slice-3 source contains a focused direct-SQL proof that, with `alveara.patient_merge = 1`, a changed MRN is refused by `51298`; it verifies the immutable-MRN trigger has no merge stand-aside clause and that the MRN remains unchanged. The selected first-trigger order makes the specific refusal deterministic, while a real merge and unmerge retain both MRNs unchanged.

The addendum also preserves the required U-016/U-017 protections: all five omitted merge-scope columns are listed; the clearance/document ordering issue is reproduced before repair; ordinary same-patient enforcement and end-of-merge edge verification remain in place; trigger-definition preservation and Down restoration are required; and the fourth migration is disclosed.

## Authorization

`AddMergeTriggerIntegrityRepairs` may be implemented as the approved fourth forward migration in ALV-N025. Run only the defined focused merge/MRN groups and controls, measured under the established policy. Preserve this addendum and disclose U-015, U-016, and U-017 in the final handoff. No broad/full regression is authorized.
