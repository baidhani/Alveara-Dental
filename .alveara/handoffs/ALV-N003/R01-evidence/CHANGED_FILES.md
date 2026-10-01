# ALV-N003 R01 — Changed Files

`git diff --stat ff0a757..848fce8` (ALV-002-C01 closure commit → ALV-N003 implementation commit): **38 files changed, 5511 insertions(+), 7 deletions(-)**.

## Backend — new

| File | Purpose |
|---|---|
| `src/Alveara.Api/Architecture/Configuration/ConfigurationEntities.cs` | `PracticeSettings`, `PracticeLocation`, `Operatory`, `AppointmentType`, `ProviderWeeklyAvailability`, `ProviderBlockedTime` |
| `src/Alveara.Api/Architecture/Configuration/ConfigurationSupport.cs` | `ConfigurationException`, audit event names, `ConfigurationWrite` (row version, same-save audit, unique-violation → 409) — the reusable write pattern |
| `src/Alveara.Api/Architecture/Configuration/PracticeConfigurationService.cs` | practice, location, operatories, appointment types |
| `src/Alveara.Api/Architecture/Configuration/StaffProviderService.cs` | staff, providers, account linkage, availability, blocked time, link picker |
| `src/Alveara.Api/Architecture/Configuration/SchedulingConfiguration.cs` | the read model + availability check scheduling consumes |
| `src/Alveara.Api/Controllers/ConfigurationController.cs` | `api/config/*` (no DELETE for configuration entities) |
| `src/Alveara.Api/Migrations/20261001035949_AddPracticeConfiguration(.Designer).cs` | schema |

## Backend — modified

`Identity/IdentityEntities.cs` (`StaffProfile.IsActive/JobTitle`, `ProviderProfile.IsActive/RowVersion`), `Identity/Permission.cs` + `PermissionMatrix.cs` (`ManagePracticeConfiguration`), `Data/AlveraDbContext.cs` (sets, indexes, `Restrict` FKs, single-active-location filtered index), `Program.cs` (DI), `Migrations/AlveraDbContextModelSnapshot.cs`.

## Backend tests — new

`PracticeConfigurationServiceTests.cs`, `StaffProviderServiceTests.cs`, `SchedulingConfigurationTests.cs`, `ConfigurationApiTests.cs` (53 cases).

## Frontend — new

`services/configApi.ts`; `hooks/useUnsavedChangesWarning.ts`; `components/ConfigEntityPanel.tsx/.css` + `validateFields.ts` (the reusable configuration panel); `pages/ConfigurationHubPage.tsx/.css`; `pages/configuration/{PracticeTab,PeopleTabs,SimpleTabs,AvailabilityTab,SchedulingPreviewTab}.tsx` + `availabilityRules.ts`.

## Frontend — modified

`App.tsx` (route `/admin/configuration`, gated `ManagePracticeConfiguration`), `app/moduleRegistry.ts` (nav entry), `services/authApi.ts` (`request`/`requestWithCsrf` exported — no behavior change).

## Frontend tests

New: `components/ConfigEntityPanel.test.tsx` (16), `pages/ConfigurationHubPage.test.tsx` (13). Extended: `App.routeGuards.test.tsx` (+2), `e2e/auth-real-backend.spec.ts` (+1 test, limited-permission test extended).

No file under `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, `GPT Docs/`, or `.alveara/QUALITY_GATES.md` was touched by the implementation commit.
