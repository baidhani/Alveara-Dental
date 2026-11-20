# ALV-N026 finding survey (step 1, read-only)

**Story:** ALV-N026 (plan item 63): documents, prescriptions, printing and backup/restore integrity (R05 PKG-12).
**Date:** 2026-11-20.
**Method:** read-only code reading at `318bf3c` (origin/main). No test was run, and no database was read or written.
- **Raw text:** each item's raw text is a line of the v6 log (`C:\.alvera-data\Manual-E2E\reviewer-package-v6\manual-e2e-findings.md`).
- **Scope source:** the 23 rows of `docs/stabilization/DISPOSITION_REGISTER.json` with `homePackage` PKG-12, plus Q-5cl's documents portion.
- **Carried in from ALV-N025:** OQ-2 (the MRN on printed documents) and U-008 (unassigned).

**Plan acceptance (item 63):** documents and signed forms keep their integrity and appropriate visibility; print output is accurate and privacy-safe; prescriptions keep their lifecycle truth; backup/restore and missing-file paths communicate safely. **PD-11, PD-24 and PD-25 must be resolved before the dependent work.**

**Governance (R05):**
- escalate if a new notification channel is added;
- a permission-model change is escalated;
- prescription content is regulatory content: research it, do not guess.

Verdict words:
- **CONFIRMED** (the code does what the finding says);
- **PARTLY ADDRESSED** (part of it is already there);
- **ANSWERED** (a question the code answers);
- **DECISION** (needs an owner decision).

## Group A: documents, storage and clinical-document access
Root cause: document actions are shown regardless of the file's state or the viewer's clinical permission.

| Id | Raw text (v6 line) | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-130 (S3) | L811: a file marked "File missing" still offers Open and Download | `DocumentEntries.tsx:53-54` renders Open and Download whatever `integrityStatus` is | CONFIRMED | No Open or Download for a Missing (or Corrupt) file; say why |
| F-131 (S3) | L812: Open on a missing file opens a raw JSON page in a new tab; "Contact support" should say who | Open is a plain `<a href target="_blank">` to `/api/documents/{id}/content`; the server answers 409 JSON `file_missing` | CONFIRMED | An in-app message instead of a raw tab (check first, or open through an app route); the message names who to contact (the practice administrator) |
| F-125 (S4) | L778: an empty filter result says "No documents yet ..." | `DocumentsPanel.tsx:143`: one EmptyState whatever the filter | CONFIRMED | "No documents match this filter" when a filter is set |
| F-171 (S4) | L1127: the Actions column wraps, with up to six buttons per row | `DocumentEntries.tsx:53-60` renders up to six inline buttons | CONFIRMED | A tidy actions layout (for example the primary actions plus a "More" menu) |
| F-173 (S3) | L1134: with ImportDocuments, an assistant also gets Edit and Inactivate on every document, including other people's and clinical ones | `DocumentsPanel.tsx:86` `canEdit: canImport`; server `PUT /api/documents/{id}`, `inactivate` and `reactivate` all require **ImportDocuments** (`DocumentsController.cs:163-177`) | CONFIRMED | **DECISION (PD-24 with permissions):** a separate edit/inactivate right, or "own imports only", or keep it with a clear grant dialog |
| Q-5cn (compliance) | L1126: a manager without ViewClinicalDocumentation can list, open, download, preview, edit and inactivate clinical files | Every document read needs only **ViewDocuments**, which every role except Unassigned holds (`PermissionMatrix.cs`). `DocumentCategory` has no clinical marker (`DocumentEntities.cs:65`) | CONFIRMED | **DECISION PD-24** |
| Q-5cl (documents part) | L1147: what does the manager's Documents tab list, clinical documents included? | As Q-5cn: everything | ANSWERED by the code | UI and direct-API assertions against the PD-24 policy |
| Q-5bj | L767: PDFs have Open and Download but no Preview, by design? | `DocumentEntries.tsx:50` Preview only for png/jpeg (an inline image); a PDF opens in the browser's own viewer | ANSWERED (by design) | Optional: say so in the row or the help text |
| Q-5bk | L776: the description is not shown in the list; the edit's history was not checked | The About column (`EntryAbout`) shows the encounter, tooth and region, not the description | CONFIRMED (the description is not listed) | Show a shortened description (or a tooltip); verify that the Details history shows edits |
| Q-5bs, F-138 | L877: no sign that a document is linked to a case; "Opened by Staff member" | The actor name is fixed by ALV-N025's rule (U-011); there is no case-link indicator in the row | PARTLY ADDRESSED | A case-link mark on the row (ALV-N022 links are typed) |
| Q-5cx | L1340: the disk holds 10 files, 8 are listed as "nothing points to them" | Not checked in code (needs the storage report's file filter) | NOT YET CHECKED | Check the orphan report's rules (subfolders, non-document files) and explain the count on the page |
| U-008 (unassigned) | Dentists cannot import documents (permission default) | Dentist lacks ImportDocuments by default | CONFIRMED (unassigned) | **Owner: assign to ALV-N026 with PD-24, or leave** |

## Group B: prescription output
Root cause: printing has no print stylesheet worth the name, no status handling beyond one word, and no researched content.

| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-108 (S3) | L585: the print preview shows 2 sheets, the second blank | `PrescriptionWorkflow.css:15-20` hides the page with `visibility: hidden` (it still takes space) and positions the printable absolutely | CONFIRMED | A print stylesheet that removes, not hides, the rest (`display: none` on the app shell during print), so the output is one sheet |
| F-109 (S3) | L586: the browser header and footer print the page title and the URL (with the patient's internal id) | No `@page` rule; the browser's own headers and footers. The app can set `@page { margin: 0 }` (most browsers then omit them) or produce a server-side PDF | CONFIRMED | `@page` margins plus the app's own header and footer inside the page; or a PDF (**owner choice**, see D-PRN) |
| F-110 (S2) | L587: no prescriber signature line, no licence/NPI number, no patient address | `PrescriptionPrintContent` (`PrescriptionLifecycleService.cs:84`) holds practice, patient name and birth date, prescriber name, medication, dose, instructions, quantity, refills, allergy statement and a content hash | CONFIRMED | **DECISION PD-11:** research the requirements (jurisdiction), then the owner decides. Adds fields to the print content and to the staff profile (licence, NPI, DEA where required) |
| F-164 (S3) | L1092: Print stays available on a cancelled or discontinued prescription | `PrescriptionRecordList.tsx:64` offers Print whenever `hasDetails`; the print shows the status word (`PrintPreview.tsx:19`) | PARTLY ADDRESSED | **DECISION:** hide Print, or keep it with a clear watermark plus date and reason |
| Q-5aq | L595: the printed discontinued prescription has no discontinue date or reason | Not in the print content | CONFIRMED | With F-164 |
| F-165 (S4) | L1093: "28 tablet" | `PrescriptionLifecycleService.cs:74` `$"{value} {unit}"`, no plural | CONFIRMED | Plural units (a small fixed table: tablet(s), capsule(s), ml stays) |
| Q-5am | L548: "Once" and "Once daily", what is the difference? | `PrescriptionRules.cs:50` lists both | CONFIRMED (wording) | Rename "Once" to "Once only (single dose)" or add a hint; existing records keep their stored value |
| Q-5ao | L566: does a second Amend create a second draft? | `PrescriptionDraftService.cs:23,160`: an open amendment is returned, so there is never a second draft | ANSWERED | No change; add a test if none exists |
| OQ-2 (from ALV-N025) | The MRN on printed patient documents, statements, receipts and prescriptions | The prescription print content is versioned by its canonical string `p1`; the financial documents by template version (v3) | DECISION SCOPE | Add the MRN: prescription content `p2` (plus F-110/F-164 fields), treatment-plan patient copy, statement and receipt template version 4, with compatibility tests (v1-v3 unchanged) |

## Group C: backup, restore and operational status
Root cause: backup alerts are written only to a folder; the restore picker hides the generated key; slow actions give no feedback.

| Id | Raw text | What the code shows | Verdict | Plan |
|---|---|---|---|---|
| F-152 | L1014: backup alerts are text files in `Backup\notifications`; nobody is told | `Program.cs:263` registers only `FileDropBackupNotifier` (`BackupSupport.cs:49`) | CONFIRMED | **DECISION PD-25:** the channel(s) and the behaviour when delivery fails (agent recommendation in R05: the in-app inbox plus a banner on administrator screens, with a durable record when delivery fails). An email channel is a new channel, so it escalates |
| F-153 (S3) | L1019: the restore wizard's file picker hides the generated `.asc` key | `RestoreWizard.tsx:143` `accept=".pem,.key,.txt"`; the key is saved as `.asc` (`RecoveryKeyPanel.tsx:70`) | CONFIRMED | Add `.asc` (or drop the filter) |
| F-154 (S3) | L1030: "Remove the restored copy" gives no busy feedback; the result message is easy to miss | `BackupLists.tsx:151` no busy or disabled state | CONFIRMED | A shared busy, disabled and idempotency-safe state with a lasting result; check Run backup and the restore drill too |
| D-06 (owner idea) | L1036: System Status should show backup and recovery, storage and staff-account status | `SystemStatusPage.tsx`: server, network, deployment, database, job runner, version. No backup or storage section | CONFIRMED (gap) | **DECISION (scope):** which of (a) backup/recovery, (b) storage, (c) staff accounts go into ALV-N026 |
| Q-5ch | L1027: can old backups be deleted? | No delete button; retention purges (keep 7, the newest restore-proven backup always kept), audited | ANSWERED (by design) | No change (recommendation recorded in the log) |

## Decisions needed before the dependent work (for the owner)
1. **PD-24, document visibility** (Q-5cn, Q-5cl, F-173; U-008 assignment): who may see clinical documents, and how a document is "clinical" (a category flag, or a per-document choice?). Also whether edit and inactivate need their own right separate from import, or are limited to the importer.
2. **PD-11, prescription content** (F-110): research the requirements for the practice's jurisdiction (licence, NPI, DEA, signature, patient address), then decide. This needs to know the practice's jurisdiction or jurisdictions.
3. **PD-25, backup alert channel** (F-152): in-app inbox plus banner (no new channel), or email too (a new channel, escalated), and the behaviour when delivery fails.
4. **F-164 / Q-5aq:** hide Print on cancelled and discontinued prescriptions, or print with a watermark, date and reason.
5. **F-109 approach (D-PRN):** browser print with `@page` rules (no new dependency), or server-side PDF (a new dependency, escalated).
6. **D-06 scope:** which System Status sections belong to ALV-N026.
7. **OQ-2 scope confirmation:** the MRN on all four printed materials, with new template and content versions.
