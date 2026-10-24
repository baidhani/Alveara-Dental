# ALV-009-C01 — Independent Review R01, Stage 1

**Decision:** APPROVED_PENDING_REGRESSION  
**Reviewed:** 2026-10-24  
**Handoff:** `Alveara_Handoff_ALV-009-C01_4b12cc1_R01.zip`  
**Package SHA-256:** `CC2C14FCAA32439E8A2B018F893D99C08070193879053536B05D6C3CD7D2D1C1`  
**Implementation reviewed:** `4b12cc19bf8de4bf97a9c5e0aab3aee95218b8ae`  
**Evidence reviewed:** `ce0bd94cc13d299b21cf1cf816510436ab9d5bf2`

## Decision basis

The parent `STORY-009` has genuine portal verification recorded before this companion begins. R01 supplies the required work-queue, recall, unscheduled-treatment, and durable reminder behavior without weakening that parent contract.

I verified the server-side controls in the reviewed code and focused evidence:

- Recall and accepted-but-unscheduled treatment only enqueue durable ALV-N002 jobs; handlers re-check current state and use stable source keys, so retry, restart, and concurrent schedulers cannot create duplicate tasks.
- System-created tasks, their immutable history entries, audit events, and job receipts are staged in the runner transaction. Unscheduled-treatment closing honors the later treatment state and leaves a person-closed task unchanged.
- Reminder delivery is job-backed, rechecks task state and due time, deduplicates by task/due/channel, recovers a claimed job after restart, and continues retrying an owed reminder after exhausted attempts.
- Work-queue reads and writes enforce `ViewFollowUps`/`ManageFollowUps`; recall-settings administration additionally requires `ManageRecallSettings`. Queue mutations use row versions, stable conflict responses, audit entries, and history records.
- Schema constraints and triggers protect task kinds, priority, sources, owners/creators, closure shape, append-only history, reminder deduplication, and recall settings/plan ranges.

The evidence is proportionate to Stage 1: focused backend **314/314**, HTTP-level API subset **860/860**, restore drill **1/1**, negative controls **20/20** killed, client gates with Vitest **1,617 in 102 files**, mocked Playwright **140**, repository checks **13**, and real-backend walkthroughs for work queue **8**, parent follow-ups **8**, notifications **10**, permission policy **7**, and permission-policy administration **8**. I independently ran the frozen Stage 2 evaluator validation; all **9/9** scenarios produced the stated pass/fail result.

## Frozen Stage 2 authorization

This approval authorizes exactly one formal Stage 2 regression after the required review-closure-only commit. The closure tree must be clean and have no `src/` differences from `4b12cc19bf8de4bf97a9c5e0aab3aee95218b8ae`.

Run once, with no other test activity:

```sh
sh .alveara/handoffs/ALV-009-C01/R01-evidence/stage2/run_stage2.sh 4b12cc19bf8de4bf97a9c5e0aab3aee95218b8ae <out-folder>
```

The evaluator must qualify all frozen substantive criteria:

- **4,896 backend passed, 0 failed, 0 skipped**;
- unchanged STORY-009 final-run IDs and outcomes plus exactly the **162** allowlisted ALV-009-C01 IDs, with nothing retired;
- one template build, zero fallbacks, at least 3,000 clones, permitted real-path fixtures, and unchanged leak counts;
- TypeScript, build, lint, Vitest **1,617 in 102 files**, mocked Playwright **140**, and repository checks **13**;
- real-backend walkthroughs **8, 8, 10, 7, and 8** for work queue, follow-ups, notifications, permission policy, and permission-policy administration.

Backend elapsed time is operational evidence only and is not a pass/fail condition. Submit the regression ZIP for final review. Do not mark ALV-009-C01 `COMPLETE` or begin ALV-009-C02 until final approval is recorded.
