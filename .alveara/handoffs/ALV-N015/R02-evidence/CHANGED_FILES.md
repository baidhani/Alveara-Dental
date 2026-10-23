# ALV-N015 R02 - Changed files (material groups)
- Schema: `Migrations/20261023035521_AddNotificationCenter*.cs`, `AlveraDbContextModelSnapshot.cs`, `Data/NotificationModel.cs`, `Data/AlveraDbContext.cs`.
- Domain and services: `Architecture/Notifications/*` (entities, views, `NotificationService`, `MessageService`, `NotificationLinkService`, `NotificationAdminService`, `NotificationConsumer`), `Architecture/Identity/Permission.cs`, `PermissionMatrix.cs`, `AuditLogEntry.cs`, `Program.cs`, `Controllers/NotificationsController.cs`.
- Client: `services/notificationsApi.ts`, `components/NotificationBell.tsx/.css`, `pages/notifications/*`, `App.tsx`, `app/AppShell.tsx`, `app/moduleRegistry.ts`.
- Tests: `NotificationSchemaTests`, `NotificationPermissionTests`, `NotificationApiTests`, `RealMigrationInventory*`, `SchedulingApiHarness` (optional `UserId`, consumer polling off), `Notifications.test.tsx`, `e2e/notifications-real-backend.spec.ts`, `playwright.notifications.config.ts`, `playwright.config.ts` (ignore list).
- Docs: `docs/NOTIFICATIONS.md`. Control: `PROGRESS.md`, `BUILD_STATE.md`, `EXECUTION_STATUS.json`, this folder.

## R02 delta (only these files changed from R01)
`NotificationViews.cs` (the list DTO loses `Subject`), `NotificationService.cs`, `notificationsApi.ts`, `NotificationsPage.tsx`, `NotificationApiTests.cs`, `Notifications.test.tsx`, `e2e/notifications-real-backend.spec.ts`, `docs/NOTIFICATIONS.md`.
