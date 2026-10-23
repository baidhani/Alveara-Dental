# ALV-007-C01 R03 - Descendant revalidation request

## Story and request
`ALV-007-C01` (reopened, corrected, now `COMPLETE` as R03) has eight declared descendants marked `REVALIDATION_REQUIRED` at the reviewer's direction: `STORY-008`, `ALV-008-C01`, `ALV-007-C02`, `ALV-008-C02`, `STORY-016`, `ALV-016-C01`, `STORY-010`, `ALV-010-C01`. The reviewer's closure said each keeps its own evidence and closure path. **This is the request to restore them, one decision per story.** `ALV-N015` (item 42) is blocked only because `STORY-010` and `ALV-010-C01` are among the eight.

## Evidence
`R03-revalidation-evidence/SUMMARY.md` and `R03-revalidation-evidence/per-story/` (one CSV per story), `R02-evidence/REVALIDATION_ASSESSMENT.md` (the impact assessment: three direct consumers of the corrected component, STORY-008 through the legacy completion path, four with no code path). Every descendant's backend tests passed in the 4,324-test run on the corrected code, plus the targeted 409 and 1,379 selections, plus the shared frontend gate. **No new test run was made for this request.**

## Decisions requested
Restore each of the eight to `COMPLETE` on this evidence, or name the extra evidence required for any of them (for example the real-backend walkthrough for `STORY-010` and `ALV-010-C01`, which was not rerun and is the only evidence type of their closures that this package does not cover; a rerun would be a deliberate, separately-authorized run).

## Limitations
See `SUMMARY.md`, "What this revalidation does not claim".

## Next-story impact
After the restorations, `ALV-N015` (item 42) is no longer blocked by these dependencies.
