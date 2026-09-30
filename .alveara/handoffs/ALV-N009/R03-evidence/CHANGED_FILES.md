# ALV-N009 R03 — Changed Files

`git diff --stat 3a4ffce..47a80c3` (review-decision-record commit → R03 implementation commit):

```
src/Alveara.Api.Tests/AuthControllerPermissionMatrixTests.cs |  28 +++++
src/Alveara.Api/Program.cs                                   |  10 ++
src/alveara-client/src/contexts/AuthContext.test.tsx         | 135 +++++++++++++++++++++
src/alveara-client/src/contexts/AuthContext.tsx              |  53 +++++---
4 files changed, 212 insertions(+), 14 deletions(-)
```

| File | Change |
|---|---|
| `src/Alveara.Api/Program.cs` | `options.SlidingExpiration = false` added to the cookie-auth configuration (ALV-N009-R02-01) — the only backend production-code change this attempt. |
| `src/alveara-client/src/contexts/AuthContext.tsx` | New `requestSeqRef` monotonic request-generation guard; `refresh()` only applies a response if no newer decision has superseded it; `logout()` and the reactive-401 handler both bump the sequence synchronously (ALV-N009-R02-02). |
| `src/Alveara.Api.Tests/AuthControllerPermissionMatrixTests.cs` | One new test proving repeated authenticated calls never extend the session expiry. |
| `src/alveara-client/src/contexts/AuthContext.test.tsx` | Two new tests forcing the exact out-of-order completions the R02 review found (stale refresh vs. a newer 401; stale refresh vs. an explicit logout). |

No file under `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, or `GPT Docs/` was touched.
