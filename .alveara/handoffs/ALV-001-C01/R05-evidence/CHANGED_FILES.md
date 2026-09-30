# ALV-001-C01 R05 — Changed Files

`git diff --stat 0ffee3c..9fd920a`: 2 files changed, 124 insertions(+), 14 deletions(-).

## Backend — production code

| File | Change |
|---|---|
| `Architecture/Identity/AccountService.cs` | `CompleteMfaChallengeAsync`'s recovery-code branch now rolls back (instead of committing) the transaction when the recovery-code conditional update affects zero rows, undoing that attempt's own challenge consumption |

## Backend — tests

| File | Change |
|---|---|
| `AccountServiceMfaTests.cs` | 1 test corrected in place (threshold math fix + expanded assertions), 2 new tests (two-challenges-one-code race, deterministic rollback reproduction) |

No migration, controller, or frontend file was changed this attempt.
