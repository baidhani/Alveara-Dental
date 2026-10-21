# Demo evidence - ALV-010-C01 R03

The visible workflow was run un-mocked: a real Chromium, the real Vite proxy, the real `Alveara.Api`, a fresh migrated SQL Server database and real files on disk (`e2e/documents-library-real-backend.spec.ts`, **12 of 12** on this tree (fresh run); the screenshots in `artifacts/walkthrough/` were taken on the identical tree `f3eb57b`). Test data only; no real patient data.

| Screenshot | What it shows |
|---|---|
| `01-library-with-context.png` | the library with a document tied to an encounter and a tooth, one to an oral region, and one to nothing; context chips on each |
| `02-filter-refused-range.png` | a bad date range (from after to) explained beside its box, nothing sent |
| `03-thumbnails.png` | the thumbnail view: pictures drawn small, a PDF named "PDF" in words; the import form with encounter, tooth and region (optional) and the filter form above |
| `04-inactive-details.png` | an inactive document with its details: history and the reason, who and when |
| `05-storage-intact.png` | the Document storage page after "Check all stored files": counts, nothing needs attention |
| `06-storage-missing.png` | a file taken from disk: 1 missing, listed under "Files that need attention" with Check again and a link to the patient's documents |
| `07-library-missing.png` | the library showing "File missing" on that document |
| `08-unreferenced-listed.png` | a stray file listed under "Stored files nothing points to" (a fresh stray file is not listed) |

Also shown by the run without a screenshot: Preview, Inactivate needing a reason and Reactivate, the dentist refused (403) and without the buttons, a removal needing a selection and a confirmation (Cancel removes nothing), the log holding no file name, reason or patient, and axe scans in light and dark of the library list, the thumbnail view and the storage page (intact and missing states): 0 critical or serious.

Not shown in a browser: the upload progress bar and Cancel (covered by `DocumentLibraryFeatures.test.tsx` with a fake XMLHttpRequest, not by a real large upload); the Document settings link to the form templates (covered by `DocumentSettingsPage.test.tsx` and by the STORY-010 walkthrough's axe scan of that page).

The default tooth numbering in the screens is the odontogram's (Universal), so FDI tooth 16 reads "Tooth 3".
