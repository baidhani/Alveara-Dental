# Repository state during the Gate B evidence run

- Branch: `main`
- Product code under test (HEAD when the evidence runs started): `17130f7c5786b397ae51f35af94b8829152284e5` (the merge of the portal sync after ALV-N004 R06 closure `078c069`)
- Gate B tooling committed afterwards: `30fe481` - only `src/alveara-client/gate-b/`, a `.gitignore` entry and `PROGRESS.md` differ from the code under test:
```
PROGRESS.md                                        |   7 ++
 src/alveara-client/.gitignore                      |   3 +
 src/alveara-client/gate-b/README.md                |  27 +++++
 src/alveara-client/gate-b/make-tablet-specs.mjs    | 129 +++++++++++++++++++++
 .../gate-b/playwright.tablet.config.ts             |  25 ++++
 5 files changed, 191 insertions(+)
```
- Working tree at the start of the run: clean (`git status --porcelain` empty)
- Every reviewed implementation / evidence / status / closure SHA of items 11-17 and of the five corrections is an ancestor of this HEAD, with one disclosed exception: the four `ALV-011-C01` SHAs (`7923986`, `34d3ddf`, `a6b2905`, `17f6258`) were re-created by a rebase before they were first pushed and are not ancestors; their on-main equivalents are `64d47bc`, `4313490`, `61c42e7`, `1c246cb` (`git diff <reviewed> <on-main> -- . ':!.colaberry' ':!artifacts'` is empty for each pair). The mapping is recorded in `.alveara/BUILD_STATE.md` (commit `851a32b`).
