# Questions on the Binding Testing Cadence Notice

**About:** `docs/design/TESTING_CADENCE_NOTICE.md` (effective 2026-10-13), as applied while packaging `ALV-008-C02`.
**Asked by:** the coding agent, after reading the notice. **Answers wanted from:** the independent reviewer.
**How to answer:** each question has the default I will follow until told otherwise. Reply with the number and "agree" or a different instruction; nothing else is needed.

## 1. Why I am asking: what the time is actually spent on

The goal behind the notice is to stop spending two to three hours after each story. Measured while packaging `ALV-008-C02` (one machine, serial runs, nothing else running):

| Step | Time | Notes |
|---|---|---|
| Full backend suite (`dotnet test`, serial) | **1 h 21 m** for 3302 tests (1 h 06 m for 3206 tests last story) | The long pole. It grows with every story. Run twice this story (1 h 23 m, then 1 h 21 m) because verification found a defect late and the full run had to be repeated on the final tree |
| Real-backend walkthrough sweep (25 fresh databases) | about 13 minutes | 266 tests |
| Frontend gates (tsc, vitest, build, lint, mocked Playwright, repository checks) | about 2 minutes | vitest 29 s, mocked Playwright 20 s |
| Negative controls (24 mutants, rebuild and rerun each) | about half an hour over three passes | My own practice for high-risk stories |
| Targeted runs of the changed subsystem while building | a few minutes per class | Used continuously during the story |

What the notice changes: for **low or medium risk** stories the full backend, frontend, repository-check and mocked-browser runs can be deferred for up to three stories. What it does not change: any story that touches a migration, billing, audit, transactions, idempotency, concurrency or test infrastructure still runs everything. Almost every story so far has touched at least one of those. STORY-016 (prescriptions) will almost certainly add a migration.

One data point, offered honestly: in this story the full backend run passed first time (3300 of 3300). The one real defect (adoption would have made an earlier statement unreproducible) was found by reading what a reprint reads, not by the full suite, and it was proved by one targeted test. That is a single story, not a pattern; across earlier stories the full run has caught my own intermittent tests and a stale expectation, and those were worth catching.

## 2. Questions

**Q1. Is the real saving meant to come from the cadence, or from making the backend suite itself faster?**
The earlier test-speed work planned a template-database approach (milestone M2, still pending) that would attack the 1 h 21 m directly, and it helps every story including high-risk ones.
*Default:* I resume M2 as a separate, governed item when told to, and treat the cadence as a complement, not the whole answer.

**Q2. What counts as "changes a migration or database schema"?**
As written, a story that only adds one nullable column with a check constraint is high risk.
*Default:* any new migration file or model change is high risk, with no size threshold. Please say if a smaller class (for example a pure index or a comment change) should count as low risk.

**Q3. Where is the count of stories since the last broad regression kept, and what is the baseline?**
*Default:* I add a line "Last full regression: item 36, implementation `5ef44f0`" to `.alveara/BUILD_STATE.md` and update it whenever a story runs the broad regression. `ALV-008-C02` is the baseline, so up to three eligible stories may follow before the next one.

**Q4. Is the real-backend walkthrough sweep part of the "broad regression"?**
The notice lists backend, frontend, repository checks and mocked browser, and names the walkthroughs for gates, releases and milestones.
*Default:* I run only the story's own required walkthrough on routine stories, plus the walkthroughs of any parent or neighbour specs I edited. I run the full sweep at gates, releases, milestones and when a story retires or changes something the walkthroughs depend on (as `ALV-008-C02` did).

**Q5. Does the notice apply to course stories (`STORY-nnn`, which have no handoff package)?**
And can a frontend-only story skip the full backend run, and a backend-only story skip the frontend runs?
*Default:* yes it applies to course stories as far as they have a "handoff"; and I run only the side a story changed, plus the repository checks, while recording that decision in `TEST_RESULTS.md`.

**Q6. What is required for attempt R02 after CHANGES_REQUIRED?**
*Default:* for a low or medium risk story, R02 runs the finding's tests, the targeted tests of the changed files and the story's earlier targeted evidence, not the broad regression. For a high-risk story, R02 reruns the full regression, because the fix changes code.

**Q7. Are negative controls (mutation testing) still expected?**
They are not mentioned in the notice and cost about half an hour. They found real gaps in this story (three missing tests).
*Default:* keep them for high-risk stories only, drop them for low and medium risk ones, and state which in `TEST_RESULTS.md`.

**Q8. If a deferred broad regression fails on the third story, which story owns the failure?**
Any of the three could have caused it, and the third story would carry a failure it did not cause.
*Default:* the third story runs the broad regression first, and if it fails I bisect by rerunning the suite on the earlier stories' commits before assigning anything; the failure is recorded in the handoff either way. I would rather the reviewer say if a different rule is wanted.

**Q9. Who judges the risk declaration?**
*Default:* I declare it in `TEST_RESULTS.md` with the reason from the notice's list. If the reviewer disagrees, the story is returned for the missing regression, the same as for any other missing evidence. For anything uncertain I declare high risk.

**Q10. Housekeeping: the four governance files.**
`HANDOFF_SCHEMA.md`, the two GPT Docs files and `TESTING_CADENCE_NOTICE.md` are changed but uncommitted. The `ALV-008-C02` handoff cites the notice (its `TEST_RESULTS.md` has the risk declaration the notice asks for), but the reviewer's ZIP does not contain the notice.
*Default:* I do not touch them. If the reviewer wants the notice inside the package or the repository history before review, the project owner commits the four files as one separate record, which does not affect the three commits of the story.

## 3. What I am not asking

Nothing in the notice relaxes a story's own required tests, and I read it that way: the `ALV-008-C02` prompt's required tests (410 and removed UI, adoption success and failure paths, parent-regression mapping, repository caller search, real-browser walkthrough) were all run regardless of the cadence.
