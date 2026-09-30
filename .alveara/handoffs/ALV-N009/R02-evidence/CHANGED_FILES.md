# ALV-N009 R02 — Changed Files

`git diff --stat b340a0c..c7ed14c` (review-decision-record commit → R02 implementation commit):

```
src/alveara-client/src/App.routeGuards.test.tsx    | 53 +++++++++++++++
src/alveara-client/src/contexts/AuthContext.test.tsx | 76 ++++++++++++++++++++++
src/alveara-client/src/contexts/AuthContext.tsx    | 45 ++++++++++++-
src/alveara-client/src/pages/LoginPage.tsx         |  5 +-
src/alveara-client/src/pages/MfaChallengePage.tsx  |  9 ++-
5 files changed, 182 insertions(+), 6 deletions(-)
```

| File | Change |
|---|---|
| `src/alveara-client/src/contexts/AuthContext.tsx` | Expiry-poll effect now calls `refresh()` at the boundary instead of only clearing the warning flag (ALV-N009-R01-01). New bounded-poll + focus/visibility revalidation effect while signed in (ALV-N009-R01-02). New `SESSION_REVALIDATION_INTERVAL_MS` constant. |
| `src/alveara-client/src/pages/LoginPage.tsx` | Passes the already-computed `redirectTo` into the MFA challenge navigation's `location.state` (ALV-N009-R01-03). |
| `src/alveara-client/src/pages/MfaChallengePage.tsx` | Reads `redirectTo` from `location.state` (defaulting to `/`) and navigates there after a successful challenge, instead of always `/` (ALV-N009-R01-03). |
| `src/alveara-client/src/contexts/AuthContext.test.tsx` | Two new fake-timer tests: expiry-boundary sign-out, bounded-poll revalidation. |
| `src/alveara-client/src/App.routeGuards.test.tsx` | One new test: protected URL → login → MFA challenge → original URL, with permission enforcement at the destination. |

No backend file (`src/Alveara.Api/`, `src/Alveara.Api.Tests/`) was touched this attempt. No file under `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, or `GPT Docs/` was touched.
