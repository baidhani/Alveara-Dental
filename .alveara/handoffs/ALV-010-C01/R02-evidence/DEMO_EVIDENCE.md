# Demo evidence - ALV-010-C01 R02

The visible workflow was run un-mocked: a real Chromium, the real Vite proxy, the real `Alveara.Api`, a fresh migrated SQL Server database and real files on disk. The new walkthrough (`e2e/documents-library-real-backend.spec.ts`) **passes 12 of 12 on this code** in the sweep. The screenshots in `artifacts/walkthrough/` are the ones committed at `be29f3b` (taken on this UI). Test data only.

| Screenshot | What it shows |
|---|---|
| `01-library-with-context.png` | the library with a document tied to an encounter and a tooth, one to an oral region, and one to nothing; context chips |
| `02-filter-refused-range.png` | a bad date range explained beside its box, nothing sent |
| `03-thumbnails.png` | the thumbnail view: pictures drawn small, a PDF named "PDF" in words; the import form with encounter, tooth and region; the filter form |
| `04-inactive-details.png` | an inactive document with its details, history and the reason |
| `05-storage-intact.png` | the Document storage page after "Check all stored files" |
| `06-storage-missing.png` | a file taken from disk: 1 missing, with Check again and a link to the patient's documents |
| `07-library-missing.png` | the library showing "File missing" |
| `08-unreferenced-listed.png` | a stray file listed under "Stored files nothing points to" |

Not shown or not proven on this tree:
- **The "Manage form templates" link on Document settings fails dark-mode contrast** (the STORY-010 walkthrough catches it).
- **The "Open the patient's documents" link on the storage page was never contrast-checked on this code** (the scan of that state was added in the abandoned `f3eb57b`).
- The upload progress bar and Cancel are covered by client tests with a fake XMLHttpRequest, not by a real large upload.
- The default tooth numbering is the odontogram's (Universal), so FDI tooth 16 reads "Tooth 3".
