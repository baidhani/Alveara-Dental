# Changed files - ALV-010-C01 R01

Implementation commit `f3eb57b9b6548f7e8fd800269011ff75ccb6e628`; the story's twelve commits are `05f3880..f3eb57b` (first `0c22bbc`). `A` = added, `M` = modified; 80 files in all.

## Backend (API) (19)
- `M` `src/Alveara.Api/Architecture/Backup/BackupRestoreService.cs`
- `M` `src/Alveara.Api/Architecture/Documents/DocumentEditService.cs`
- `M` `src/Alveara.Api/Architecture/Documents/DocumentEntities.cs`
- `M` `src/Alveara.Api/Architecture/Documents/DocumentImportService.cs`
- `A` `src/Alveara.Api/Architecture/Documents/DocumentIntegrityService.cs`
- `A` `src/Alveara.Api/Architecture/Documents/DocumentLibraryFilter.cs`
- `M` `src/Alveara.Api/Architecture/Documents/DocumentLibraryService.cs`
- `M` `src/Alveara.Api/Architecture/Documents/DocumentMapper.cs`
- `M` `src/Alveara.Api/Architecture/Documents/DocumentMetadataChecker.cs`
- `A` `src/Alveara.Api/Architecture/Documents/DocumentStatusService.cs`
- `M` `src/Alveara.Api/Architecture/Documents/DocumentViews.cs`
- `M` `src/Alveara.Api/Architecture/Identity/Permission.cs`
- `M` `src/Alveara.Api/Architecture/Identity/PermissionMatrix.cs`
- `M` `src/Alveara.Api/Architecture/Storage/IBlobStorage.cs`
- `A` `src/Alveara.Api/Controllers/DocumentStorageController.cs`
- `M` `src/Alveara.Api/Controllers/DocumentsController.cs`
- `M` `src/Alveara.Api/Data/AlveraDbContext.cs`
- `M` `src/Alveara.Api/Data/DocumentModel.cs`
- `M` `src/Alveara.Api/Program.cs`

## Migration (3)
- `A` `src/Alveara.Api/Migrations/20261020173300_AddDocumentLibrary.Designer.cs`
- `A` `src/Alveara.Api/Migrations/20261020173300_AddDocumentLibrary.cs`
- `M` `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs`

## Backend tests (17)
- `A` `src/Alveara.Api.Tests/ApiHarnessStorageCleanupTests.cs`
- `A` `src/Alveara.Api.Tests/DocumentBackupRestoreTests.cs`
- `A` `src/Alveara.Api.Tests/DocumentContextServiceTests.cs`
- `A` `src/Alveara.Api.Tests/DocumentIntegrityServiceTests.cs`
- `A` `src/Alveara.Api.Tests/DocumentLibraryApiTests.cs`
- `A` `src/Alveara.Api.Tests/DocumentLibraryFilterTests.cs`
- `A` `src/Alveara.Api.Tests/DocumentLibrarySchemaTests.cs`
- `A` `src/Alveara.Api.Tests/DocumentOrphanServiceTests.cs`
- `A` `src/Alveara.Api.Tests/DocumentPathSafetyTests.cs`
- `M` `src/Alveara.Api.Tests/DocumentPermissionTests.cs`
- `A` `src/Alveara.Api.Tests/DocumentStorageApiTests.cs`
- `M` `src/Alveara.Api.Tests/DocumentTestBase.cs`
- `A` `src/Alveara.Api.Tests/DocumentTestStorageCleanupTests.cs`
- `M` `src/Alveara.Api.Tests/SafetyTestBase.cs`
- `M` `src/Alveara.Api.Tests/SchedulingApiHarness.cs`
- `M` `src/Alveara.Api.Tests/SignedFormSnapshotTests.cs`
- `A` `src/Alveara.Api.Tests/TestStorageCleanup.cs`

## Frontend (UI) (25)
- `M` `src/alveara-client/src/App.patientWorkspaceRoutes.test.tsx`
- `M` `src/alveara-client/src/App.tsx`
- `M` `src/alveara-client/src/app/moduleRegistry.ts`
- `A` `src/alveara-client/src/pages/documents/DocumentContextFields.tsx`
- `A` `src/alveara-client/src/pages/documents/DocumentDetailsPanel.tsx`
- `M` `src/alveara-client/src/pages/documents/DocumentEditForm.tsx`
- `A` `src/alveara-client/src/pages/documents/DocumentEntries.tsx`
- `A` `src/alveara-client/src/pages/documents/DocumentFilters.tsx`
- `M` `src/alveara-client/src/pages/documents/DocumentImportForm.tsx`
- `A` `src/alveara-client/src/pages/documents/DocumentInactivateForm.tsx`
- `A` `src/alveara-client/src/pages/documents/DocumentLibraryFeatures.test.tsx`
- `M` `src/alveara-client/src/pages/documents/DocumentSettingsPage.test.tsx`
- `M` `src/alveara-client/src/pages/documents/DocumentSettingsPage.tsx`
- `A` `src/alveara-client/src/pages/documents/DocumentStoragePage.test.tsx`
- `A` `src/alveara-client/src/pages/documents/DocumentStoragePage.tsx`
- `M` `src/alveara-client/src/pages/documents/Documents.css`
- `M` `src/alveara-client/src/pages/documents/DocumentsPanel.test.tsx`
- `M` `src/alveara-client/src/pages/documents/DocumentsPanel.tsx`
- `A` `src/alveara-client/src/pages/documents/documentContext.ts`
- `A` `src/alveara-client/src/pages/documents/documentFilter.ts`
- `M` `src/alveara-client/src/pages/documents/documentText.ts`
- `A` `src/alveara-client/src/pages/documents/documentsTestHarness.ts`
- `M` `src/alveara-client/src/services/authApi.ts`
- `M` `src/alveara-client/src/services/documentsApi.ts`
- `A` `src/alveara-client/src/test/fakeXhr.ts`

## Real-backend walkthrough and config (3)
- `A` `src/alveara-client/e2e/documents-library-real-backend.spec.ts`
- `M` `src/alveara-client/playwright.config.ts`
- `A` `src/alveara-client/playwright.documents-library.config.ts`

## Documentation and evidence (11)
- `M` `docs/DOCUMENTS.md`
- `M` `docs/testing/REAL_BACKEND_E2E.md`
- `A` `docs/testing/documents-library-walkthrough/01-library-with-context.png`
- `A` `docs/testing/documents-library-walkthrough/02-filter-refused-range.png`
- `A` `docs/testing/documents-library-walkthrough/03-thumbnails.png`
- `A` `docs/testing/documents-library-walkthrough/04-inactive-details.png`
- `A` `docs/testing/documents-library-walkthrough/05-storage-intact.png`
- `A` `docs/testing/documents-library-walkthrough/06-storage-missing.png`
- `A` `docs/testing/documents-library-walkthrough/07-library-missing.png`
- `A` `docs/testing/documents-library-walkthrough/08-unreferenced-listed.png`
- `A` `docs/testing/documents-library-walkthrough/documents-library-evidence.json`

## Repository records (2)
- `M` `.alveara/story_catalog.json`
- `M` `PROGRESS.md`
