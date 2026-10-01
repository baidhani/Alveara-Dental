# ALV-N003 R02 — Changed Files

`git diff --stat 682d574..4d4f3c3` (R01 review-decision record → R02 implementation commit): **26 files changed, 1693 insertions(+), 149 deletions(-)**.

## Backend

| File | Change |
|---|---|
| `Architecture/Identity/IdentityEntities.cs` | `ProviderProfile.AvailabilityRevision` (versioned schedule aggregate) |
| `Data/AlveraDbContext.cs` | `AvailabilityRevision` configured as a concurrency token, default 0 |
| `Migrations/20261001045233_AddProviderAvailabilityRevision(.Designer).cs`, `AlveraDbContextModelSnapshot.cs` | new migration (adds the column) |
| `Architecture/Configuration/StaffProviderService.cs` | `GetAvailabilityScheduleAsync`; `ReplaceAvailabilityAsync` now requires/compares/bumps the revision atomically; conflict entity type `ProviderAvailability` |
| `Architecture/Configuration/SchedulingConfiguration.cs` | DST-safe availability check (`crosses_dst_transition`, constant-offset wall-clock arithmetic) |
| `Controllers/ConfigurationController.cs` | availability GET/PUT carry `{ revision, windows }` |

## Backend tests

New `AvailabilityConcurrencyTests.cs` (5, plus the `ReplaceCurrentAsync` test helper); `SchedulingConfigurationTests.cs` (+4 DST regressions); `ConfigurationApiTests.cs` (+1 revision contract, end-to-end test passes revision); `StaffProviderServiceTests.cs` (existing availability tests adapted to the revision contract, no assertions weakened).

## Frontend

| File | Change |
|---|---|
| `App.tsx` | `createBrowserRouter`/`RouterProvider` (routes unchanged); root layout hosts the banner and `NavigationGuard`; `UnsavedChangesProvider` |
| `app/NavigationGuard.tsx`, `contexts/UnsavedChangesContext.tsx`, `contexts/unsavedChangesStore.ts` | new: router-level blocker and the app-wide unsaved-edits registry |
| `hooks/useUnsavedChangesWarning.ts` | also registers with the registry; clears on unmount |
| `app/AppShell.tsx` | sign-out asks before discarding a draft |
| `pages/configuration/AvailabilityTab.tsx` | provider-bound state, numbered loads, revision + conflict recovery, failed-reload keeps draft |
| `services/configApi.ts` | `AvailabilitySchedule`; revision on replace |
| `components/ConfigEntityPanel.tsx` | `reloadAfterConflict` keeps the draft on a failed read, distinguishes inactivated/missing |
| `components/ConcurrencyConflictBanner.tsx` | reload button is `type="button"` |

## Frontend tests

New `App.unsavedNavigation.test.tsx` (5); `ConfigurationHubPage.test.tsx` (+4, existing availability tests updated for the contract); `ConfigEntityPanel.test.tsx` (+3 and a stronger conflict test); `e2e/auth-real-backend.spec.ts` (+1 real-dialog test).

No file under `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, `GPT Docs/`, or `.alveara/QUALITY_GATES.md` was touched by the implementation commit.
