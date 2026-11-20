# ALV-N025 MRN Rollback Addendum R01 — Independent Review

**Decision:** APPROVED

**Reviewed package:** `Alveara_Briefing_ALV-N025_b0c9f37_ADDENDUM-R01.zip`  
**Package SHA-256:** `65F616ACC11C0B7D11908081ED1FF7AB577A11AC81D05CCF047C93183AD6C786`  
**Scope:** MRN rollback/preservation design amendment only.

## Decision

The approved MRN design is amended as follows:

- `AddPatientMrn` may roll down without refusing merely because patients were created after its Up.
- Its Down must archive every `(PatientId, Mrn)` atomically into the durable, append-only, non-application-exposed `PatientMrnArchive` before dropping the live MRN column and its guards.
- Its Up restores archived numbers first and allocates only the remaining patients, never reusing an archived MRN.
- The retained archive table is an intentional, disclosed exception to a byte-for-byte pre-migration schema after Down. It is not an application identity source and must remain absent from normal APIs and exports.
- `51300` is retired for the MRN migration and remains available for the access-audit migration's destructive-Down refusal. `51298` and the new `51302` guards enforce MRN immutability and archive preservation/conflict refusal.

## Evidence obligations

Implement and run the addendum's focused migration proofs: populated Up; Down-to-Up exact restoration; archive conflict and deletion refusal; archive-on-insert with no orphan on failed registration; allocator avoidance of archived-only MRNs; retained-table disclosure; and the existing migration-test rollback pattern. The implementation must use the three-forward-migration packaging already approved for ALV-N025.

The addendum controls wherever it conflicts with the earlier `AddPatientMrn` Down wording in the base briefing. Preserve it with the final handoff and explicitly disclose the retained archive table.

No broad/full regression is authorized by this approval. Continue under the focused-test policy.
