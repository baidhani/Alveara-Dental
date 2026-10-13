# Testing Cadence — Independent Review Response

**Responds to:** `docs/design/TESTING_CADENCE_QUESTIONS.md`  
**Effective:** 2026-10-13  
**Authority:** the Execution Plan, Engineering & Product Reference, and `.alveara/HANDOFF_SCHEMA.md`

## Decisions

| Question | Decision | Binding instruction |
|---|---|---|
| Q1 | APPROVE | The cadence is a complement, not the primary solution to a growing serial backend suite. Do not resume template-database M2 informally. Submit it as a separate, governed test-infrastructure proposal with correctness, isolation, cleanup, equivalence, and performance evidence before implementation. |
| Q2 | APPROVE WITH CHANGE | Any new migration or persistent EF/database model change is high risk, regardless of size. This includes a nullable column, check constraint, index, trigger, or data migration. A source-code comment with no generated migration or persistent model effect is not a schema change. |
| Q3 | APPROVE WITH CHANGE | Record `Last successful broad regression` in `BUILD_STATE.md`, including item, implementation SHA, date, suites, and the count of eligible stories since it. The baseline must be on the approved current lineage. ALV-008-C02 R01 is not a baseline because it is `CHANGES_REQUIRED`; after an approved R02 has a successful full regression and durable closure, it becomes the next baseline. |
| Q4 | APPROVE | The real-backend sweep is not part of routine broad regression. Run the story's required walkthrough plus any changed parent/neighbour walkthroughs. Run the full sweep at gates, releases, declared milestones, and when a story changes shared walkthrough dependencies or explicitly requires it. |
| Q5 | APPROVE WITH CHANGE | The cadence applies to course stories as engineering validation, while the portal's current acceptance and verification remain authoritative. A routine frontend-only or backend-only story may run focused checks only on the changed side, plus required repository checks and parent tests. At the third eligible-story batch boundary, the broad regression includes both backend and frontend suites, repository checks, and mocked browser tests. A story-level prompt can require more. |
| Q6 | APPROVE WITH CHANGE | An R02 must rerun all evidence affected by its correction. A high-risk R02 requires the full broad regression before handoff. A low/medium-risk R02 runs finding, changed-subsystem, acceptance, and parent evidence, unless it is the third eligible story or another broad-regression trigger applies. Required real-browser evidence is rerun when affected. |
| Q7 | APPROVE WITH CHANGE | Ordinary negative tests remain required wherever material failure, authorization, integrity, or concurrency behavior applies. Mutation/negative-control campaigns are not automatic for every high-risk story; require them when the prompt, approved design, gate, or a new guard/invariant makes them the proportionate proof. Record whether they ran and why. |
| Q8 | APPROVE WITH CHANGE | Run the broad regression before packaging the third eligible story. If it fails, stop packaging, preserve the result, and bisect on the three candidate implementation commits before assigning ownership. Reopen or require revalidation only for the responsible story or stories; do not attach an unexplained prior failure to the third story. |
| Q9 | APPROVE | The coding agent declares risk and cites the applicable criterion in `TEST_RESULTS.md`; uncertain classification is high risk. The independent reviewer may require missing evidence, but should apply the declared policy rather than invent a post-hoc category. |
| Q10 | APPROVE WITH CHANGE | The Execution Plan, Engineering & Product Reference, Handoff Schema, and Testing Cadence Notice must be committed as a separate governance record before the policy is used for a later story. The notice need not be retroactively inserted into the immutable ALV-008-C02 R01 ZIP. Its R01 full-regression evidence remains valid because the story is independently high risk. This response and the question document are retained with that governance record. |

## Operating rule

No cadence decision weakens an individual story's explicit tests, parent contract, quality gate, portal requirement, or the independent review requirement. The current ALV-008-C02 R01 remains `CHANGES_REQUIRED`; its R02 must follow the high-risk path.
