# ALV-008-C01 Limitations and Legacy Retirement Review Response

**Decision:** APPROVED WITH CHANGES
**Date:** 2026-10-11
**Authority:** The authoritative execution plan now contains Item 36, `ALV-008-C02 — Retire Legacy Completion Paths and Adopt Historical Completions`. This response explains the decision and the resulting execution order.

## Decisions

| Point | Decision |
|---|---|
| R1 — Retirement before Gate D | **Approved.** `ALV-008-C02` is Item 36, immediately after ALV-007-C02 and before STORY-016. It must complete and receive independent review closure before STORY-016 can start. |
| R1a — Deprecation signals in ALV-008-C01 | **Approved with change.** Add only the PHI-free internal `legacy_completion_used` structured log, using request trace/activity ID, route, and outcome. Do not add a `Deprecation` response header or otherwise alter legacy HTTP responses in ALV-008-C01. |
| R2 — Acceptance-time limitation | **Approved.** Keep the acceptance-time rule for ALV-008-C01. Its validation response must explain that it compares against the recorded acceptance timestamp. A distinct agreed-on-date capability is future work. |
| R3 — Ambiguous chart match | **Approved with change.** The review screen may show readiness before submission, but must consume server-produced readiness data rather than reimplement matching rules in the browser. Return only minimal details needed to identify the ambiguity. The server remains authoritative and refuses `ambiguous_chart_finding`. |
| R4 — Adoption of legacy completions | **Approved with change.** Adoption belongs only in ALV-008-C02. It is explicit, one procedure at a time, audited, idempotent, concurrency-safe, and all-or-nothing with chart and billing effects. It must require provider, same-patient encounter, performed time, and adoption reason. |

## ALV-008-C02 non-negotiable outcomes

- No production route or UI control may create a new state-only completion after retirement.
- The old completion state/audit history remains preserved; adoption adds linked authoritative history and never rewrites it.
- Adoption reuses an existing manual charge or creates exactly one billable charge through `IProcedureCharging` in the same transaction.
- Parent regression intent must be mapped test-by-test to the authoritative workflow or an isolated test fixture. Do not alter course-portal verification state.
- The retirement handoff must include a caller search and Gate D reconciliation proving zero orphaned charges, zero missing charges among authoritative/adopted completions, and matching odontogram state.

## Execution order

1. Complete and close ALV-008-C01.
2. Complete and close ALV-007-C02.
3. Execute, review, and close ALV-008-C02.
4. Only then begin STORY-016, now Item 37.

The updated Item 36 in `GPT Docs/Alveara_Dental_Execution_Plan_v4.md` is binding. No retirement implementation belongs in ALV-008-C01 except the approved internal usage event.