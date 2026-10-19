# Documents: import, categorization and metadata (STORY-010)

> **Status: design note, written before any code.** The notification center and person-to-person messaging discussed with the owner are **not part of this story**: they are specified in `docs/design/NOTIFICATION_CENTER_AND_MESSAGING_PROPOSAL.md`, approved by the reviewer on 2026-10-19 as `ALV-N015` (item 41; `docs/design/NOTIFICATION_CENTER_AND_MESSAGING_REVIEW_RESPONSE.md`), and built later on top of this story's event history, without changing it (see *Seam for the notification story*). Nothing described here is built yet. It records the decisions taken with the project owner on 2026-10-19 so they can be reviewed first. It becomes the user-facing description of the feature when the story is finished.

Requirement **REQ-015**: *The system must import and categorize documents and forms, supporting e-signature and metadata linkage.*

## What this story delivers, and what it leaves

`STORY-010` is the thin, working version of the document library: import a file, categorize it, link it to a patient with metadata, show the patient's signed forms next to it, and audit every action. `ALV-010-C01` (item 40) later builds the full library on top of it (thumbnails and filters, imaging types, encounter and tooth-region metadata, storage reconciliation, backup/restore drills, form template administration). That story must keep this story's tests passing, so the contracts below are written to be extended, not replaced.

| Acceptance criterion | How it is met |
|---|---|
| A document is imported, then categorized: it is linked with the correct metadata | Import stores the file and its metadata in one step; recategorizing changes the category and date/description with a history row. Tests read the stored row back. |
| A form is e-signed, then the signed version is preserved | Already true since `ALV-N010` (immutable `SignedFormSnapshot`). The library lists each signed form as a read-only entry that points at the snapshot. Tests show its hash and content do not change after the template is edited or the form is voided. |
| All document actions are logged with user and timestamp | Every import (a duplicate import is marked as one), recategorization, metadata edit, open or download and every settings change writes an audit entry and, for documents, a history row, in the same save as the action. |

## Decisions (owner, 2026-10-19)

1. **Who may import and categorize:** admin and office manager. Everyone who can view the patient's record can view documents (including billing). Letting other roles import "when an admin grants it" needs a grant mechanism the system does not have; it is a separate piece of work (see *Deferred*).
2. **File types:** any type is accepted **except** a blocked list. `.exe`, `.html` and `.js` are blocked for everyone, admins included, and can never be removed. Close relatives are blocked too: `.htm .bat .cmd .com .msi .scr .ps1 .vbs .dll .jar .hta .lnk`. An admin can add more blocked types later; the built-in ones cannot be removed.
3. **Size:** one configurable maximum, default **100 MB**, stored in the database and editable by an admin.
4. **Categories:** a database table, editable by an admin, seeded with: Radiograph, Photo, Referral letter, Medical clearance, Lab report, Insurance card, Identification, Correspondence, Other.
5. **Signed forms:** a read-only library entry that points at the existing immutable snapshot. No copy is made.
6. **Duplicates:** importing the same file again for the same patient is **warned about first**. The importer must explicitly choose "Add anyway"; the addition is then recorded as a duplicate in the document history and the audit log. Notifying the office manager and the admin is delivered by the separate notification story, which reads that history (see *Seam for the notification story*); this story builds no bell, inbox, banner or message.

## The model (new tables)

| Table | Purpose | Key rules |
|---|---|---|
| `DocumentCategories` | The categories. `Id`, `Code` (stable, unique, never changes), `Name`, `SortOrder`, `IsActive`, `RowVersion`. | Seeded by the migration with fixed ids and codes (running it twice adds nothing). Never deleted: a category is deactivated. A document keeps its category when it is later deactivated; only new imports and recategorizing are refused it. |
| `Documents` | One imported file. `Id`, `PatientId`, `CategoryId`, `DocumentDate`, `Description` (up to 500 characters), `DuplicateOfDocumentId` (set only when the importer confirmed adding a duplicate), `OriginalFileName` (**metadata only**, sanitized, up to 255 characters), `DetectedType`, `ContentType` (decided by the server, never taken from the client), `SizeBytes`, `Sha256`, `BlobId` (from `IBlobStorage`), `ImportedByUserId`, `ImportedAtUtc`, `IdempotencyKey`, `RowVersion`. | The file itself is immutable: patient, blob, hash, size, original name and importer/time cannot change (application guard **and** a database trigger). Only category, date and description can be edited. A document is never deleted by this story. |
| `DocumentEvents` | Append-only history with a monotonic `EventNumber` (assigned under a lock that is held until the transaction commits, so numbers become visible in commit order, are never reused and leave no gap; see *Seam for the notification story*): `Imported`, `ImportedAsDuplicate` (of which document), `Categorized` (from, to), `MetadataEdited`, `Opened`. `PatientId` and, for a duplicate, the duplicated document's id. User and time on every row. | No update, no delete (trigger). Holds category codes and ids, never a filename, description or content. |
| `DocumentEventCounter` | One row: `LastEventNumber`. | Every write of a `DocumentEvents` row increments it **inside the same transaction**, as late as possible (`UPDATE ... SET LastEventNumber = LastEventNumber + 1 OUTPUT ...`). The row lock it takes is held until commit, so event writes are ordered by commit, a lower number can never become visible after a higher one, and a rolled-back write leaves no gap. Document events are rare, so serializing the counter costs nothing noticeable. |
| `DocumentSettings` | One row: `MaxUploadBytes` (default 104857600), `RowVersion`. | Validated 1 MB to 1 GB. |
| `BlockedFileExtensions` | `Extension` (lower case, no dot, unique), `IsBuiltIn`, `AddedByUserId`, `AddedAtUtc`. | The 15 built-in rows are seeded and cannot be changed or deleted (trigger and guard). Admin-added rows can be removed. |

Signed forms are **not** stored in `Documents`. The library reads them through the existing `PatientFormReader.SignedDocumentsAsync` (the `SignedFormDocumentDescriptor` seam built for this purpose) and merges them into the patient's list as entries of type "Signed form".

## What happens to a file on import (in this order)

1. **Permission and CSRF** are checked, and an `Idempotency-Key` is required.
2. **Size:** an empty file is refused; a file over the configured maximum is refused before it is stored (the request limit follows the setting).
3. **Name:** the user's filename is normalized (path parts removed, control characters and reserved device names refused) and kept as metadata only. It never decides where the file is stored or what it is called on disk.
4. **Blocked type:** the final extension is compared case-insensitively, after removing trailing dots and spaces and any `:stream` suffix, against the blocked list. Double extensions such as `report.pdf.exe` are therefore caught by their last extension.
5. **Content check:** a file whose content is a Windows program (an `MZ` header with a valid `PE` header) is refused whatever it is named. For a file that claims to be a PDF, PNG, JPEG or TIFF, the first bytes must match; a mismatch is refused. Any other type is accepted and labelled "type not verified".
6. **Category and metadata** are validated (the category must exist and be active, the date must be a real date no later than today, the description within its length).
7. **Store, then check for a duplicate:** the file is stored through `IBlobStorage` (generated identity, atomic write, SHA-256). If that patient already has a document with the same SHA-256 and the request does not carry `confirmDuplicateOf`, the stored file is removed, nothing is recorded, and the caller receives `409 duplicate_document` with a summary of the existing document (id, category, date, who imported it and when). The importer can then upload again with `confirmDuplicateOf` set to that document's id, which the server checks really is a same-content document of the same patient.
8. **Record:** in one database transaction, the `Documents` row, the history row (`Imported`, or `ImportedAsDuplicate`), the audit entry (`DocumentImported` or `DocumentDuplicateImported`) and the idempotency receipt are saved together. If anything after the file write fails, the stored file is removed and the caller gets a clear error with **nothing recorded**. A failed audit write fails the import.

A duplicate warning stores nothing and uses up no idempotency key, so the confirmed retry may reuse the key. The same request repeated with the same key returns the same document and changes nothing. The same key with a different file or different metadata is refused (`idempotency_key_reused`).

## What happens when a file is opened

Every file is served as a **download**: `Content-Disposition: attachment`, a generic content type and `X-Content-Type-Options: nosniff`. The only exceptions are PDF, PNG and JPEG, which may be shown inline under a restrictive `Content-Security-Policy: sandbox`. A TIFF is always a download (browsers do not display it). The type shown comes from the server's own detection. Opening writes an `Opened` history row and an audit entry.

## API (all under the existing conventions: CSRF on writes, row version on edits, stable `error` codes)

| Route | Purpose | Permission |
|---|---|---|
| `POST /api/patients/{id}/documents` | Import (multipart: file, category, date, description). `201` on create, `200` on an exact replay. | `ImportDocuments` |
| `GET /api/patients/{id}/documents` | The patient's library: documents and signed forms, optional category filter. | `ViewDocuments` |
| `GET /api/documents/{id}` | One document's metadata and history. | `ViewDocuments` |
| `GET /api/documents/{id}/content` | The file, under the serving rules above. Logs the open. | `ViewDocuments` |
| `PUT /api/documents/{id}` | Categorize / edit date and description (needs the row version). | `ImportDocuments` |
| `GET /api/document-categories`, `POST`, `PUT /{id}` | List, add, rename, deactivate or reactivate a category. | read: `ViewDocuments`; write: `ManageDocumentSettings` |
| `GET /api/document-settings`, `PUT` | The maximum size. | `ManageDocumentSettings` |
| `GET /api/document-settings/blocked-extensions`, `POST`, `DELETE /{ext}` | The blocked list. Built-in entries cannot be deleted. | read: `ViewDocuments`; write: `ManageDocumentSettings` |

Stable error codes include: `file_empty`, `file_too_large`, `file_type_blocked`, `file_type_mismatch`, `executable_content`, `file_name_invalid`, `category_invalid`, `category_inactive`, `metadata_invalid`, `duplicate_document` (409), `duplicate_confirmation_invalid`, `idempotency_key_required`, `idempotency_key_reused`, a standard `409` conflict for a stale row version, and `storage_failed` / `audit_unavailable` (`503`, nothing saved).

## Permissions (added to the existing fixed role table)

| Permission | Roles |
|---|---|
| `ImportDocuments` | Admin, Office manager |
| `ViewDocuments` | Admin, Office manager, Front desk, Dentist, Hygienist, Assistant, Billing |
| `ManageDocumentSettings` | Admin |

## Audit

Audit names are `DocumentImported`, `DocumentDuplicateImported`, `DocumentCategorized`, `DocumentMetadataEdited`, `DocumentOpened`, `DocumentCategoryAdded`, `DocumentCategoryRenamed`, `DocumentCategoryDeactivated`, `DocumentCategoryReactivated`, `DocumentSizeLimitChanged`, `BlockedExtensionAdded` and `BlockedExtensionRemoved`, each with the user and a timestamp. Audit entries carry **ids and codes only**: the document's own id, the action and a category code. They never carry a patient id, a patient name, a filename, a description or file content. A filename can contain a patient's name, and a description is clinical text, so neither is ever copied into the audit log or the diagnostic logs.

## Screens

- **Patient workspace, Documents tab:** the list (name, category, date, size, imported by and when, with signed forms labelled "Signed form"), a category filter, an import form (file, category, date, description) that shows each refusal beside its field, open/download, and for admin and office manager a metadata edit.
- **Duplicate warning:** when an import is refused as a duplicate, the screen names the existing document (category, date, who and when) and offers **Add anyway** or **Cancel**. Add anyway re-sends the same file with the confirmation.

- **Admin, Document settings:** categories (add, rename, deactivate), the size limit and the blocked list. Built-in blocked types are shown as locked.

## Failure paths (the story's list, and what happens)

| Failure | Behaviour |
|---|---|
| Incorrect categorization | An unknown or deactivated category is refused with its field named; recategorizing keeps the old category in the history. |
| Metadata error | Every invalid field is listed and linked; nothing is stored and the typed values stay on screen. |
| E-signature failure | Unchanged from `ALV-N010`: signing is one atomic, idempotent save; an interrupted signing leaves a plain draft. The library never shows a half-signed form. |
| Audit failure | The action fails and nothing is saved; for an import the stored file is removed. |
| Duplicate import | Refused with a warning that names the existing document, nothing stored; allowed only with an explicit confirmation that is verified server-side; then recorded as a duplicate in the history and the audit log in the same save. |
| Concurrency | Edits carry a row version; a stale edit is a `409` that changes nothing. Two imports with the same key create one document. |

**Failure-first answers.** If an import fails after the file was written, the file is removed in the same request; no automatic retry happens (the caller repeats the request, which is safe because of the key). If the process dies between the file write and the database commit, an orphaned file can remain on disk with no record pointing to it; it is harmless and not served, but it is not cleaned up by this story. Detecting and removing such files, and detecting a record whose file went missing, belongs to `ALV-010-C01`. The failure modes handled here are listed in the table above; storage reconciliation, malware scanning and very large files are not handled.

## Tests this story adds (written before the code)

Validators (name, extension rules, executable detection, type match, size), the duplicate path (a duplicate is refused without a confirmation and stores nothing; a forged or wrong-patient confirmation is refused; a confirmed duplicate is recorded as one, with the duplicated document's id, in the history and the audit log), the event history seam (`EventNumber` strictly increasing with no gap and no reuse, including under concurrent imports and after a rolled-back import; a reader that has seen a number has seen every lower one; rows immutable; ids and codes only, no filename or description in any column), the service (import, replay, wrong key, categorize, concurrency, a failing store, a failing audit save), the database (triggers on immutable columns, history and the locked blocked list, seeded categories present once), the API (unauthenticated, each role, CSRF, error codes, content served as a download), signed-form preservation (template edit and void leave the entry unchanged) and a real-backend browser walkthrough of the Documents tab and the admin settings.

## Duplicates

A duplicate means the **same file content (SHA-256) already filed for the same patient**. The same file for a different patient is not a duplicate (a family can share a form), and is not warned about. The warning, the explicit "Add anyway" and the duplicate record are described under *What happens to a file on import*.

## Seam for the notification story

The owner wants a notification for the office manager and the admin whenever a duplicate is added anyway, and later a general notification center and messages between users. That is a separate story (`docs/design/NOTIFICATION_CENTER_AND_MESSAGING_PROPOSAL.md`). So that **nothing in this story has to change** when it is built, this story guarantees only the following, which it needs for its own history and audit anyway:

1. `DocumentEvents` rows of kind `ImportedAsDuplicate`, with these columns (named here because `ALV-N015` reads them): `EventNumber`, `Kind`, `DocumentId` (the new copy), `RelatedDocumentId` (the document it duplicates), `PatientId`, `ByUserId`, `AtUtc`.
2. **`EventNumber` is safe to use as a cursor.** It is assigned from `DocumentEventCounter` under a row lock held until commit (see *The model*), so a reader that has seen number N has seen every event numbered below N: no event can commit late with a lower number. A plain database identity column would not give that guarantee, because two concurrent imports can commit out of order and a consumer would skip the earlier one for good.
3. Rows are immutable and hold ids and codes only, so a consumer needs no other knowledge of documents: no document name, filename, description or content is in any column. The `PatientId` in this history is **internal**: it exists so a consumer can build an authorization-checked link. It is not written to the audit log, and the notification story's own policy (decision 12 of its review) is that a notification list never returns a raw patient id; the link is resolved on the server and opens the patient only for a user who may view that patient.
4. **A stable page for that link to open:** the Documents tab keeps the route `/patients/{patientId}/documents`, optionally with `?document={id}` to highlight one document. A link followed by someone without `ViewDocuments` is refused by this story's own permission check, so the link cannot be used to see more than that person may see.

The notification story reads these rows after they are committed and creates the notifications itself (a notification may therefore appear a few seconds after the import). This story defines no notification table, route, permission or screen.

## Deferred: notification center and internal messaging (a separate story)

Specified in `docs/design/NOTIFICATION_CENTER_AND_MESSAGING_PROPOSAL.md` and approved by the reviewer on 2026-10-19 as `ALV-N015`, item 41, after `ALV-010-C01` and before `STORY-009` (`docs/design/NOTIFICATION_CENTER_AND_MESSAGING_REVIEW_RESPONSE.md`): a bell with an unread count for every signed-in person, a history that can be deleted or cleared, a dismissible banner shown only for Important or Urgent notifications, messages between users (for example a dentist to an assistant), and the first system producer, this story's duplicate-document event. It is not built here.

## Deferred: admin-grantable permissions (a separate story)

The owner wants an admin to be able to configure any permission for any role, and the office manager to be able to grant only the document-import permission to other roles. Today permissions are fixed in one table in code and the permission-matrix screen is read-only, so this is a new authorization-administration feature, not part of this story. It would need: stored role grants (audited), delegated granting limited to named permissions, every permission check (`RequirePermission`, `RequireAnyPermission`, the permissions endpoints the UI reads, the matrix screen and its tests) reading the merged result, and a security review. This story's three permissions are defined so they can move into that system without rework. The proposal is recorded here for the project owner to schedule.

## Open points for the owner

1. The 12 "relative" blocked types are locked like the three you named (an admin cannot remove them). If a practice needs one allowed, that is a code change, by design.
2. TIFF files are download-only, not shown inline.
3. HTML and JavaScript cannot be reliably recognized from their content, so for those the extension rule is the only protection (a file renamed to another type would be stored as a download-only file, never rendered).
4. A duplicate is the same content for the **same patient** only; the same file for a different patient is accepted without a warning.
5. The duplicate notification itself is delivered by the separate notification story, not by this one. Until that story exists, a confirmed duplicate is visible in the document's history and in the audit log, but nobody is notified.
