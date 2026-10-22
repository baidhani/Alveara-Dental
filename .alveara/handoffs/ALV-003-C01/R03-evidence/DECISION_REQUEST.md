# Question for the reviewer - how the pair is closed after this item

The reviewer directed: after this item's focused Stage 1 review, rerun one shared frontend-only gate "for this pair and the test corrective". `ALV-N016` R04 and `ALV-007-C01` R03 are both `CHANGES_REQUIRED` (their Stage 2 failed). Under `HANDOFF_SCHEMA.md` that starts a new attempt of each (`ALV-N016` R05, `ALV-007-C01` R04).

**Question:** may the shared gate be recorded as an **additive Stage 2 re-run attached to R04 and R03** (their Stage 1 approvals and the reuse evidence stand; only the frontend gate failed), or do you want two new no-source-change Stage 1 resubmissions (R05 and R04) first? I will do whichever you state; without a statement I will prepare the resubmissions (the more cautious reading), which costs only paperwork, not machine time.
