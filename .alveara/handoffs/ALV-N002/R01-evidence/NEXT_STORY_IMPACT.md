# ALV-N002 R01 — Next Story Impact

## Assumptions the next stories can rely on

- **`AlveraDbContext`** (`src/Alveara.Api/Data/AlveraDbContext.cs`) is the single DbContext. New entity types are added as new `DbSet<T>` properties here, with their own `OnModelCreating` configuration block, and a new migration via `dotnet ef migrations add <Name>`.
- **Money** must always be represented as `Alveara.Api.Architecture.Money.Money`, never a raw `decimal`/`double` amount, in any new financial entity or DTO.
- **All appointment-local time input** must go through `IPracticeClock.FromPracticeLocal`, never a raw `DateTime` constructor — this is what makes DST handling explicit rather than accidental.
- **`UserAccount`/`StaffProfile`/`ProviderProfile`** already exist; `STORY-001`/`ALV-001-C01` should populate `UserAccount.PasswordHash` and add authentication logic against the existing `UserAccounts` table rather than creating a parallel one.
- **New background work** registers an `IBackgroundJobHandler` and calls `IBackgroundJobQueue.EnqueueAsync` with a real idempotency key — it does not need to touch `BackgroundJobRunner`/`BackgroundJobHostedService` at all.
- **New measurement events** call `IMeasurementEventSink.RecordAsync`; a genuinely new property key must be added to `MeasurementEventValidator.AllowedPropertyKeys` in the same change, after confirming it cannot carry PHI.
- **New document/image storage** should use `IBlobStorage`, not raw `File.WriteAllBytes` calls scattered through new code.
- **The System Status page's structure is stable**: new status facts (e.g. `ALV-N004`'s backup-drill-freshness) should add a new card to `SystemStatusPage.tsx` and a new field to `SystemStatusController`'s response, not restructure the existing ones.

## Interfaces the next story will touch

- **`STORY-001`/`ALV-001-C01`** (secure authentication/RBAC) — populates `UserAccount.PasswordHash`, adds `Role`/`RolePermission` tables linked from `UserAccount`, and is the first real consumer of the identity-separation schema.
- **`ALV-N004`** (encrypted backup/restore) — consumes `IBackupSnapshotProvider` directly; should also back up the blob-storage root (`IBlobStorage`'s `storageRoot`) as a persistent asset class, not just the database.
- **`ALV-N009`** (authorization-aware navigation) — extends `moduleRegistry.ts`'s `ModuleDefinition` shape (e.g. adding a `requiredPermission` field) rather than replacing the registry.

## Migrations

`InitialArchitecture` is the only migration so far. Future stories add their own migrations on top of it via `dotnet ef migrations add <Name>` from `src/Alveara.Api`.

## Unresolved decisions relevant to the next story

- The exact SQL Server Express install/configuration steps for the real Windows-server deployment (vs. this story's LocalDB dev setup) are `ALV-N013`'s decision, not made here.
- Whether `PracticeClock`'s DST-ambiguity resolution is exposed to the front-desk user (asking them to pick) or resolved by a fixed policy is left to `ALV-004-C01` (the scheduler), which is the first real caller of `FromPracticeLocal`.
