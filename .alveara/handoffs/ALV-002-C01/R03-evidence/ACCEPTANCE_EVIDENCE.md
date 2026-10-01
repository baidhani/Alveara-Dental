# ALV-002-C01 R03 — Acceptance Evidence

| Acceptance criterion | Evidence |
|---|---|
| Original STORY-002 tests still pass unchanged | Unchanged, no pre-existing assertion modified — full backend suite 199/199. |
| Existing account/role changes use the shared audit path | Unchanged from R01. |
| A representative currently-existing mutable record demonstrates optimistic concurrency/stale-edit rejection | Unchanged (`StaffProfile.RowVersion`). |
| Shared lifecycle/idempotency primitives are unit-tested without pretending nonexistent clinical/financial records are already finalized | Unchanged from R01/R02. |
| Audit failure policy and audit/business-write coupling are explicit and tested | Unchanged from R02 (`AuditLogImmutabilityTests.A_business_change_and_its_new_required_audit_event_are_both_rolled_back_when_the_audit_INSERT_genuinely_fails`). |
| Later domains can adopt the shared primitives | Unchanged. |
| Required UI/UX delivery | **Completed (R02-01, R02-02).** The viewer now shows data the real API actually returns, and requests the window it advertises. |

## R02 findings closed this attempt

| Review finding | Correction | Evidence |
|---|---|---|
| ALV-002-C01-R02-01 (P2): real audit API omits entity metadata the viewer expects | `AuthController.GetAuditLog` projects `EntityType`, `Reason`, `CorrelationId` (legacy/no-metadata rows stay `null`) | `AuthControllerPermissionMatrixTests.GET_audit_log_round_trips_entity_type_reason_and_correlation_id`; real-browser test 7 asserts the real `SessionTimeoutChanged` row visibly contains `UserAccount` |
| ALV-002-C01-R02-02 (P2): viewer claims 500 events but requests 100 | `AUDIT_LOG_WINDOW = 500` constant drives both the request (`?take=500`) and the description text | `AuditLogPage.test.tsx` request-window contract test; `AuthControllerPermissionMatrixTests.GET_audit_log_defaults_to_100_rows_and_honors_take_up_to_500` (130 staged events: default 100, `take=500` all 130, `take=9999` clamped to ≤500) |

## Previously closed findings (R02 review: CLOSED), retained unchanged

ALV-002-C01-R01-02 (conflict presentation), -03 (genuine audit-INSERT failure test), -04 (test accounting/timestamps), and the preserve-and-clarify items (immutability-guard scope comment, receipt-specific idempotency classifier).
