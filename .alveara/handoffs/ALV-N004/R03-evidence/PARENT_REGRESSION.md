# ALV-N004 R03 — Parent Regression

**N/A — justified** (New Production story, no course parent).

Dependency / targeted revalidation: ALV-001-C01 MFA/protected state (legacy discriminator compatibility and recorded-name recovery tests pass; no authentication code changed), ALV-N002 (blob storage, jobs, topology, migrations; no schema change this attempt), ALV-N003 time-zone semantics (non-default zone recovery test), startup behaviour (WebApplicationFactory suites run through the verified-before-serve guard and the early-verification path; real-backend Playwright 12/12), ALV-002-C01 and ALV-N009 suites - all within the 366-test full run.
