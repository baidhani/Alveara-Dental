# ALV-N003 R03 — Next Story Impact

## Pattern change (supersedes R02's "provider-bound async UI pattern")

Completion protection for any editor whose subject or contents can change while requests are in flight must use **editing-session identity and request identity, not domain identity**:

1. Keep a session generation (a ref counter) that advances whenever the editor is (re)populated — selecting a subject *and* every successful (re)load.
2. Every request captures the session at issue time; its success, error, notification and follow-up refresh are discarded unless that session is still current. Provider/record id alone cannot work: A → B → A makes an abandoned session's response look current again.
3. Keep request identity (load/request sequence numbers) separate from the domain id.
4. Decide a pending-write policy per editor and test it: either disable inputs while saving (generic panel, practice form) or capture a draft counter at submit and keep newer typing, advancing only the saved baseline and revision (availability editor). Never let a response replace edits entered after the snapshot it submitted, and never clear a form that was edited after submit.
5. Test with hand-completed held responses, including the A → B → A → new draft → old response sequence and the error path.

`AvailabilityTab` and `ConfigEntityPanel` are the reference implementations.

## Carried forward (unchanged)

Versioned weekly-schedule API (`{ revision, windows }`, shared 409), conservative DST rule (`crosses_dst_transition`), app-wide unsaved-change registry + router-level `NavigationGuard`, failed-reload draft preservation, `ConcurrencyConflictBanner` non-submitting button, `SchedulingConfiguration` read model, `ManagePracticeConfiguration` gating.

## For `ALV-N004`

Backup/restore must treat the configuration tables as persistent assets and preserve `ProviderProfiles.AvailabilityRevision` together with the weekly windows (and the other `RowVersion` tokens' semantics on restore).

## Gate A

Row A8 can be evaluated once this story is approved; it is not evaluated or marked here. Gate A as a whole still waits for `ALV-N004`.

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before item 10 (`ALV-N004`) begins.
