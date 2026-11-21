# ALV-N026 issue-row ownership decision

**Decision:** Prescription and treatment-plan-copy issue rows reach the patient through their parent record. They do **not** carry a redundant `PatientId`.

## Applies to

| Issue row | Ownership rule |
|---|---|
| `PrescriptionIssue` | Stores `PrescriptionId` only. The patient is obtained through the prescription. |
| `TreatmentPlanCopyIssue` | Stores `PlanId` only. The patient is obtained through the treatment plan. |

`ClinicalRecordCopyIssue` remains directly patient-owned because it has no prescription or treatment-plan parent. Existing `FinancialDocument` ownership remains unchanged.

## Required implementation rules

1. Use enforced foreign keys to the parent prescription or treatment plan.
2. Resolve patient access, patient scope, audit association, and query filtering through the parent record. Do not trust a caller-supplied patient identifier.
3. Preserve the existing printed identity/practice envelope in each issue row; it represents displayed historical text, not ownership.
4. Do not add these two issue tables to `PatientOwnedData`, merge registry entries, patient-scope guards, or merge triggers solely for ownership.
5. Patient merge behavior is inherited from the already tested parent relationship: when the parent prescription or plan is reassigned by an approved merge, its issue rows resolve to the surviving patient through that parent without an issue-row update.
6. Keep append-only guards on the issue rows. Test that direct updates/deletes are refused and that, after an approved merge of the parent, the issue remains reachable only through the surviving patient's parent record.

## Rationale

`PatientId` would duplicate a relationship that the parent already owns. Redundant patient ownership would require new merge-scope triggers, stand-aside logic, registry entries, and direct-SQL proof—precisely the shared infrastructure where prior merge-guard regressions occurred. The parent-only design preserves correct patient scope while reducing schema and merge risk.

## Documentation update

This decision supersedes the direct-`PatientId` portions of the ALV-N026 brief for `PrescriptionIssue` and `TreatmentPlanCopyIssue`. Update the brief/data model and disclose this deliberate deviation in the S6/S7 handoff. No additional design review is required for this change.
