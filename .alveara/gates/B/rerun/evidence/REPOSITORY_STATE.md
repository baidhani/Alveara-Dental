# Repository state for the Gate B rerun

- Branch: `main`
- **Final head under test: `13504387a9ae123f9014c872cf7ade8f74e74ae9`** (clean working tree at the start of the run; `status-at-start` empty). Everything except the backend suite was run on this head.
- Backend suite run at `b0620bfdda1962be9dc1f2700569ca0b72ea700d`; that head differs from the final head only by `PROGRESS.md` and `src/alveara-client/e2e/auth-real-backend.spec.ts`; no file under `src/Alveara.Api`, `src/Alveara.Api.Tests` or `src/alveara-client/src` differs:
```
PROGRESS.md                                      | 7 +++++++
 src/alveara-client/e2e/auth-real-backend.spec.ts | 7 ++++++-
 2 files changed, 13 insertions(+), 1 deletion(-)
```
- Commits since the first evaluation (`e0a8629`, evaluation record; product code `17130f7`; tooling `30fe481`):
```
1350438 Gate B test stabilization: bounded wait on server-side recovery-key generation in the auth walkthrough
b0620bf Merge portal build-plan sync into main
6f0c4a4 Gate B tooling: vitest excludes every gate-*/ directory; coexistence regression check (B-REV-01)
c416114 Gate B: record independent review - FAIL / CHANGES_REQUIRED (B-REV-01)
94e8b30 chore(colaberry): sync build plan — 3 files
```
- The four `ALV-011-C01` reviewed SHAs (`7923986`, `34d3ddf`, `a6b2905`, `17f6258`) are not ancestors (re-created by a rebase before first push; on-main `64d47bc`, `4313490`, `61c42e7`, `1c246cb`; mapping in `.alveara/BUILD_STATE.md`). Every other reviewed SHA of items 11-17 and of the five pre-Gate-B corrections is an ancestor.
