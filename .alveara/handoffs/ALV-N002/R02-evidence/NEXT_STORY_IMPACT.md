# ALV-N002 R02 — Next Story Impact

Carries forward R01's `NEXT_STORY_IMPACT.md` in full (repository path: `.alveara/handoffs/ALV-N002/R01-evidence/NEXT_STORY_IMPACT.md`). This file adds only what changed in R02.

## New facts the next stories can rely on

- **`IBackgroundJobHandler.ExecuteAsync` now takes a `transactionalDb` parameter.** Any new background-work handler must write its local-database effect through this context and must **not** call `SaveChangesAsync` itself — the runner commits the effect, a completion receipt, and the job's status together, in one transaction. A handler whose effect is not representable as a row in this database (a future external-system call) is responsible for its own idempotency (check-before-act), using `transactionalDb` to read/write its own "already done" marker if needed.
- **A new migration pattern exists**: `MigrationUpgradeTests.cs`/`MigrationFailureTests.cs` demonstrate how to test a real upgrade (seed on an older migration, upgrade, verify data survives) and a real migration failure (a throwaway test-only `DbContext`/migration pair, never touching the real schema). Later stories adding their own migrations should follow the upgrade-test pattern for any migration that changes an existing populated table.
- **`MeasurementEventValidator`'s allow-list now requires a per-key validator function**, not just a key name. Any story adding a new measurement-event property must add both the key *and* its type/bounded-value rule to `PropertyValidators` in the same change — the key alone is not enough for the property to be accepted.
- **The System Status page has a `stale` state** in addition to `loading`/`error`/`loaded`. Later stories extending `SystemStatusPage.tsx` (e.g. `ALV-N004`'s backup-drill-freshness card) should render inside `StatusGrid`, which both the `loaded` and `stale` states already share — not duplicate the grid layout.
- **Real LAN reachability was proven via this machine's actual network interface** (`192.168.1.180` in this environment; will differ per deployment). `ALV-N013`'s Windows installation story should document how to determine and bind the correct LAN-facing interface on the target server, rather than assuming `0.0.0.0` alone is sufficient operational guidance.
- **A least-privilege SQL login pattern now exists** (`LeastPrivilegeAccessTests.cs`) that `ALV-N013`/`ALV-N014` can adapt into the actual production service-account provisioning script/documentation — this story proved the *application* needs only `db_datareader`+`db_datawriter`; it did not provision the real production login itself (that's deployment, not architecture).

## Unresolved from R01, still unresolved

- Database engine choice was already made (SQL Server Express/LocalDB) — unchanged, not reopened by this review.
- Production same-origin deployment strategy remains `ALV-N013`'s decision.
- DST-ambiguity-resolution UX (front-desk-visible disambiguation vs. fixed policy) remains `ALV-004-C01`'s decision.

## New for R02

- **Production Windows Service account configuration and CA-issued TLS certificate deployment/trust for LAN clients are explicitly NOT established by this story** — recorded as a limitation in `BUILD_STATE.md` and `R02.md`, not silently assumed solved. `ALV-N013` (installation) and `ALV-N014` (security/privacy readiness) own closing this gap for the actual release.
