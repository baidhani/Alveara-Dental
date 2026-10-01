# ALV-002-C01 R02 — Acceptance Evidence

| Acceptance criterion | Evidence |
|---|---|
| Original STORY-002 tests still pass unchanged | Unchanged, untouched this attempt — full backend suite 197/197. |
| Existing account/role changes use the shared audit path | Unchanged from R01. |
| A representative currently-existing mutable record demonstrates optimistic concurrency/stale-edit rejection | Unchanged from R01 (`StaffProfile.RowVersion`). |
| Shared lifecycle/idempotency primitives are unit-tested without pretending nonexistent clinical/financial records are already finalized | Unchanged from R01, extended with `IdempotencyGuardTests.IsDuplicateReceiptViolation_is_true_for_the_receipt_s_own_unique_index_and_false_for_an_unrelated_failure`. |
| Audit failure policy and audit/business-write coupling are explicit and tested | **Corrected (ALV-002-C01-R01-03).** `AuditLogImmutabilityTests.A_business_change_and_its_new_required_audit_event_are_both_rolled_back_when_the_audit_INSERT_genuinely_fails` forces a real SQL Server PK violation on a new audit event staged through the actual shared path. |
| Later domains can adopt the shared primitives | Unchanged from R01. |
| Required UI/UX delivery | **Corrected (ALV-002-C01-R01-01, -02).** `AuditLogPage.tsx` + `ConcurrencyConflictBanner.tsx`, both tested and documented below. |

## R01 findings closed this attempt

| Review finding | Correction | Evidence |
|---|---|---|
| ALV-002-C01-R01-01 (P1): audit viewer missing | `AuditLogPage.tsx`, routed `/admin/audit-log`, gated `ViewAuditLog`, nav entry, loading/empty/error/denied states | `AuditLogPage.test.tsx` (4 tests); `App.routeGuards.test.tsx` (2 new tests); real-backend Playwright "the audit log page shows real audit entries this session produced, as the admin" |
| ALV-002-C01-R01-02 (P2): conflict presentation missing | `ConcurrencyConflictBanner.tsx` + `authApi.isConcurrencyConflict` | `ConcurrencyConflictBanner.test.tsx` (4 tests) |
| ALV-002-C01-R01-03 (P2): audit-failure test didn't exercise real persistence failure | New genuine-PK-violation test through `AuditService.Record` | `AuditLogImmutabilityTests.A_business_change_and_its_new_required_audit_event_are_both_rolled_back_when_the_audit_INSERT_genuinely_fails` |
| ALV-002-C01-R01-04 (P2): manifest/handoff metadata inaccurate | Corrected math (184 + 11 R01 + 2 R02 = 197) and post-commit `generatedAt` | This handoff; `MANIFEST.json` |

## Preserve-and-clarify items addressed

| Note | Correction | Evidence |
|---|---|---|
| Immutability-guard comment overclaimed coverage of bulk-update/raw SQL | `AlveraDbContext.cs`'s guard comment now explicitly scopes to change-tracked `SaveChanges` calls | `src/Alveara.Api/Data/AlveraDbContext.cs` |
| Idempotency doc didn't distinguish the receipt's own violation from an unrelated failure | `IdempotencyGuard.IsDuplicateReceiptViolation` added | `IdempotencyGuardTests.IsDuplicateReceiptViolation_is_true_for_the_receipt_s_own_unique_index_and_false_for_an_unrelated_failure` |
