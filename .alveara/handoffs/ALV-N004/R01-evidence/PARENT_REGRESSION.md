# ALV-N004 R01 — Parent Regression

**N/A — justified.** `ALV-N004` is a New Production story with no course parent (`parentCourseStory: null`).

Dependency regression, all passing with no pre-existing assertion weakened:

- **`ALV-N002`** (architecture): reuses `IBackupSnapshotProvider` (its existing test still passes), `IBlobStorage`/`LocalDiskBlobStorage` and the durable background-job runner (`BackgroundJobTests`, `BlobStorageTests`, `StorageIsolationTests`, migration/upgrade tests, PHI-safe logging and topology tests all pass). The new migration applies on top of the prior schema.
- **`ALV-001-C01`** (auth): one change to identity code — `AccountService` gained `ReauthenticateAsync` (step-up through the existing lockout boundary) and exposes the MFA protector purpose as a public constant; the Data Protection **application name is now fixed**. All MFA/lockout/session/RBAC tests pass; the permission enum was appended (existing ordinals undisturbed) and the role→permission tests pass with the two new permissions.
- **`ALV-002-C01`**: audit, concurrency (`BackupSettings` row version + shared 409), idempotency tests pass; the audit viewer shows the new events.
- **`ALV-N003`**: configuration tables are inside the database backup (so `ProviderProfiles.AvailabilityRevision` and weekly windows are preserved); all configuration tests pass.
- **`ALV-N009`**: the new route follows the same `RequirePermission` pattern; route-guard, navigation-guard, shell and session suites pass unchanged.

Full results: backend 343/343, frontend 156/156, real-browser 12/12.
