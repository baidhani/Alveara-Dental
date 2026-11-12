# ALV-N020-C01 R01: next-story impact

Next scheduled item: ALV-N021 (item 58), not started and not authorized until ALV-N020-C01 is `COMPLETE`. ALV-N029 stays deferred until after ALV-N027.

- **Account-scoped permissions** now exist (`AccountPermission`, `AccountPermissionGrants`, `RequireAccountPermission`, `accountPermissions` in `/api/auth/permissions`). A future per-account permission is added to the `AccountPermission` enum (and the database check `CK_AccountPermissionGrants_Permission` by migration); it is never added to `Permission`.
- **Bootstrap response** gains `presentationAdministrator` and `notice`; existing readers keep working.
- **Backups** include a fifth asset class `loginBackgrounds`; tests that assert the exact class list (backup history rows, missing-class lists) must include it.
- **Sign-in pages** render inside `LoginLayout` (route level in `App.tsx`); tests that render the full app at `/login` see one extra anonymous request (`/api/public/login-presentation`), which falls back silently when a stub answers something else.
- **ALV-N029 (rotation)** builds on `LoginPresentationSettings` and the catalog (bundled keys plus active added images, each with its display mode); the selection triggers (51277/51279) already protect an automatically chosen added image.
- **Deployment (ALV-N013):** the installer documentation should record `LoginPresentation:DefaultFooterNotice` and that the bootstrap administrator becomes the presentation administrator; an existing installation's operator grants it explicitly.
- **Test baseline for ALV-N028:** new classes `LoginBackgroundPipelineTests`, `LoginNoticeRulesTests`, `LoginPresentationApiTests`, `LoginPresentationSchemaTests`, `LoginBackgroundBackupRestoreTests`, `LoginBackgroundCatalogTests`; client `LoginLayout.test`, `SignInScreenTab.test`, `accountPermissionGating.test`; browser `e2e/login-presentation.spec.ts`.
