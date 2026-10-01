# ALV-N004 R02 — Parent Regression

**N/A — justified.** `ALV-N004` is a New Production story with no course parent.

Dependency / targeted revalidation (as the review required for `ALV-001-C01` and `ALV-N002/N003`):
- **ALV-001-C01 MFA / protected state:** no authentication code changed in R02; the Data Protection registration is back to the pre-implementation behavior (no forced name); an MFA secret protected by the legacy registration is proven to decrypt after upgrade and after recovery with the recorded name; all existing MFA, lockout, session and RBAC tests pass in the 358-test run.
- **ALV-N002:** blob storage, background jobs, topology and migration tests pass; the new migration applies on top of the prior schema.
- **ALV-N003 time-zone semantics:** recovery regression added — the recovered application resolves practice-local times to the same UTC instants only under the recorded zone, and refuses domain calls otherwise.
- **ALV-002-C01, ALV-N009:** audit, concurrency, route-guard, shell and session suites pass.
