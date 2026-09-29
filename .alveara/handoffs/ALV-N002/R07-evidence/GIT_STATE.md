# ALV-N002 R07 — Git State

## Branch

`main`

## Implementation SHA

`967b1e8c16ebc7e986c4a5e7b3d00909641080d2` (verification harness), with a same-attempt follow-up fix `c780277c` (encoding correction) applied before the harness was run against either VM.

## Evidence SHA

Not yet known at the time this file was committed — this file is part of the evidence commit itself (Git self-reference prohibition). The generated ZIP's `MANIFEST.json` and its own separately generated copy of `evidence/GIT_STATE.md` record it once the evidence commit exists.

## Snapshot integrity method (addressing N002-R06-04)

The R06 review found packaged repository snapshots differed from evidence-commit blobs by line-ending normalization when compared with the reviewer's own tooling. Independent investigation on this end (`git cat-file -p <blob-sha>` compared directly against the R06 ZIP's packaged bytes) did not reproduce a mismatch for any of the ten repository-derived files — all ten matched their raw blob exactly. Rather than debate the discrepancy, this attempt's packaging step is built directly on `git cat-file -p <blob-sha>` (the raw stored object, bypassing any possible working-tree filter path `git show` could take depending on configuration) for every repository-derived package member, and verifies the resulting ZIP bytes against that same raw blob before being reported as valid. This is the strictest possible verification method available from git itself.

## Working-tree state

Clean immediately before and after this attempt's commits.

## Relevant commits (oldest → newest)

```
c333805 ALV-N002: record R06 handoff — real server topology closes acceptance items 1 and 7 (R06 evidence)
69bb51c ALV-N002: record R06 review decision (CHANGES_REQUIRED)  (R06 review recorded, valid stop-for-review)
967b1e8 ALV-N002 R07: add versioned server/client deployment verification harness (R07 implementation)
c780277 ALV-N002 R07: fix mangled em-dash characters in verification scripts (R07 implementation, same attempt)
<this commit> ALV-N002: record R07 handoff and committed verification evidence (R07 evidence)
```
