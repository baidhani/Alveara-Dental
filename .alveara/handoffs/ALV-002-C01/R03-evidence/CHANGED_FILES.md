# ALV-002-C01 R03 — Changed Files

`git diff --stat ffcbde2..cc053da` (R02 review-decision-record commit → R03 implementation commit):

```
 src/Alveara.Api.Tests/AuthControllerPermissionMatrixTests.cs | 62 +++++++++++++++++++++
 src/Alveara.Api/Controllers/AuthController.cs                |  5 ++
 src/alveara-client/e2e/auth-real-backend.spec.ts             |  5 ++
 src/alveara-client/src/pages/AuditLogPage.test.tsx           | 15 ++++++
 src/alveara-client/src/pages/AuditLogPage.tsx                |  4 +-
 src/alveara-client/src/services/authApi.ts                   |  9 +++-
 6 files changed, 96 insertions(+), 4 deletions(-)
```

| File | Change |
|---|---|
| `src/Alveara.Api/Controllers/AuthController.cs` | `GetAuditLog` projection also returns `EntityType`, `Reason`, `CorrelationId` (R02-01). Gate, ordering, bound unchanged. |
| `src/alveara-client/src/services/authApi.ts` | New `AUDIT_LOG_WINDOW = 500`; `getAuditLog()` requests `?take=500` (R02-02). |
| `src/alveara-client/src/pages/AuditLogPage.tsx` | Description text generated from `AUDIT_LOG_WINDOW` (R02-02). |
| `src/alveara-client/src/pages/AuditLogPage.test.tsx` | One new test: the request URL carries `take=500` and the description matches. |
| `src/alveara-client/e2e/auth-real-backend.spec.ts` | Test 7 additionally asserts the real `SessionTimeoutChanged` row visibly shows `UserAccount` (R02-01). |
| `src/Alveara.Api.Tests/AuthControllerPermissionMatrixTests.cs` | Two new API tests: metadata round-trip (R02-01); default-100 / `take=500` / clamp window (R02-02). |

No file under `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, or `GPT Docs/` was touched. The evidence commit touches only `.alveara/` files.
