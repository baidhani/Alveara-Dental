# ALV-001-C01 R06 — Changed Files

`git diff --stat 70f27e1..d6fd281`: 2 files changed, 159 insertions(+), 81 deletions(-).

## Backend — production code

| File | Change |
|---|---|
| `Architecture/Identity/AccountService.cs` | Three new `internal Func<Task>?` test-only coordination seams (`TestHook_BeforeStepUpConditionalReset`, `TestHook_AfterRecoveryCodeVerifiedBeforeConsumption`, `TestHook_AfterRecoveryCodeConsumedBeforeCommit`), each null and a no-op unless a test sets it. No behavioral change to any production code path. |

## Backend — tests

| File | Change |
|---|---|
| `AccountServiceMfaTests.cs` | 3 uncontrolled-race tests (superseded, from R04/R05) removed; 4 deterministic tests added using the new coordination seams |

No migration, controller, or frontend file was changed this attempt.
