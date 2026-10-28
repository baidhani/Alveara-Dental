# Patient data portability (STORY-018)

**Status: built and verified for STORY-018.** The export (`GET api/patients/{id}/export`, the "Export patient file" button on a patient's page) and the import (`POST api/patients/import`, the "Import a patient" page under the admin menu) are in the system; the walkthrough screenshots are in `docs/testing/patient-portability-walkthrough/`.

A practice must be able to take one patient's record out of Alveara in a format other systems and people can read, and to bring such a record in, without silently accepting a bad file. This slice covers one patient's registration data. The whole-chart export package (clinical, financial and document data, with a manifest and file checks, for one patient or every patient) is the companion story ALV-018-C01: see "The export package" below. Bulk CSV import with a migration workbench is another companion story (ALV-018-C02), built and described in `docs/BULK_IMPORT.md`.

## Who may do it

| Action | Permission | Held by default |
|---|---|---|
| Export a patient | `ExportPatientData` | Administrator only |
| Import a patient | `ImportPatientData` | Administrator and Office Manager |

Both are ordinary (not protected) permissions, so an Administrator can grant them to other roles later through the permission policy screens. Front Desk can still register a patient one at a time; it does not get the file import.

## The export file

One UTF-8 JSON document, `application/json`, downloaded as `alveara-patient-<exportId>.json`:

```json
{
  "format": "alveara.patient-export",
  "formatVersion": 1,
  "exportId": "3f0c2b3e-8d5a-4a49-9a77-0e3f1f6a7b10",
  "exportedAtUtc": "2026-10-26T14:05:11Z",
  "source": { "system": "Alveara Dental", "sourcePatientId": "b2c1f4a0-0e1d-4a9e-8f3b-52d6a1c9e7aa" },
  "patient": {
    "firstName": "Ann",
    "middleName": null,
    "lastName": "Rivera",
    "dateOfBirth": "1984-03-09",
    "sex": "Female",
    "phone": "(555) 010-2299",
    "email": "ann.rivera@example.com",
    "addressLine1": "12 Elm Street",
    "addressLine2": null,
    "city": "Dallas",
    "state": "TX",
    "postalCode": "75001"
  }
}
```

- `format` and `formatVersion` identify the document so any reader can tell what it is. A reader that does not know a version must refuse it, not guess.
- `exportId` is new for every export and is what makes an import repeatable (below). `exportedAtUtc` is the time the export was made.
- `source.sourcePatientId` is informational (where the record came from); an import never uses it as an id.
- `patient` holds exactly the identity and contact fields the registration form holds, with the same names, text forms and limits as the registration rules (`PatientInput`): dates are `yyyy-MM-dd`, optional fields may be `null`.
- **Not in the file:** user account data, passwords or keys, internal row versions, household and guarantor links (they point at other records in this database), and clinical, financial and document data (those are in the export package, below). A patient who was merged away is not exported (the answer names the record that carries on).

## The import rules

A file is checked completely before anything is stored. Every problem found is reported at once, each with a stable code, the place in the file and a plain message, so the person can fix the file and send it again. Nothing partial is ever stored.

The file is the request body of `POST api/patients/import` (CSRF token required; no other header). A file is rejected (HTTP 400, `error: "import_rejected"`, with `problems`: a list of `code`, `path` such as `patient.dateOfBirth`, and a plain `message`) when:

| Problem | Code |
|---|---|
| larger than 256 KB, empty, or not valid UTF-8 JSON | `file_too_large`, `file_empty`, `not_json` |
| `format` missing or not `alveara.patient-export` | `wrong_format` |
| `formatVersion` missing, not a whole number, or not a version this system reads (only 1) | `unsupported_version` |
| `exportId` missing or not a GUID | `export_id_invalid` |
| `exportedAtUtc` not a UTC time such as `2026-10-26T14:05:11Z`; `source.sourcePatientId` not a GUID | `exported_at_invalid`, `source_patient_id_invalid` |
| a member missing, unknown, repeated or of the wrong type (at the top, in `source` and in `patient`) | `missing_member`, `unknown_member`, `duplicate_member`, `wrong_type` |
| any patient field missing or invalid under the registration rules (including the practice's own required fields) | `field_invalid` (one per field, with the field name) |

If the file is valid, the import registers the patient through the same registration service the front desk uses, so the same protections apply:
- **Repeatable:** the registration key is derived from the file's `exportId`; importing the same file again returns the patient already created (HTTP 200, `created: false`) and creates nothing. A new patient is HTTP 201 with `created: true`.
- **Exact duplicate** (same name and date of birth as an existing record, which includes importing a file exported from this very system): refused (409 `duplicate_patient`) with a pointer to the existing record; nothing is created.
- **Likely duplicate** (the existing duplicate detector's suggestions): refused (409 `possible_duplicate`) with the candidates. This slice never creates a record over a likely duplicate; the person registers it from the form, where each candidate is acknowledged (a batch file's likely duplicates are decided one by one in the Data import duplicate queue, never merged: `docs/BULK_IMPORT.md`).
- An import only ever creates a new patient. It never overwrites, merges or edits an existing record.

## The log

Every export and every import writes an audit entry with the user id and the time (the audit trail's own timestamp), in the same save as the action:
- `PatientExported` (the patient id, the export id, the format version);
- `PatientImported` (the patient id, the export id, the format version; a repeat of the same file is logged too, saying that nothing new was created);
- `PatientImportRejected` (the export id when the file had one, the number of problems and their codes), written for rejected files and for refused duplicates too.

Audit text never contains names, contact details or the file's content. If the audit entry cannot be written the whole action fails: no file is returned by an export, and an import creates no patient (the registration and its `PatientImported` entry are one database transaction).

## Failure modes this slice handles and does not

Handled: a file that is not portable JSON of this format and version; wrong, missing or extra members; invalid or missing fields; an oversized or empty file; a repeat of the same file; a duplicate; a caller without the permission (403) or not signed in (401); a failed audit write.
Not handled in this slice: more than one patient per file, clinical or financial data, linked files (documents), household and guarantor links, a signature or encryption of the file, and updating an existing patient from a file.

## The export package (ALV-018-C01)

**Status: built.** The Export patient data screen (`/admin/exports`), the three downloads under `api/patient-exports`, the readable record and the export history are in the system. The walkthrough, its screenshots, the saved packages and an independent check of them are in `docs/testing/patient-export-package-walkthrough/`.

The STORY-018 file above carries one patient's registration data and is unchanged. A **package** takes out the whole chart: every part of the record the system holds for a patient, a page a person can read, the document files and a manifest that says exactly what is and is not there, so an export never looks complete when something was left out.

### Who may do it

| Action | Permission | Held by default |
|---|---|---|
| One patient's package (and the STORY-018 file) | `ExportPatientData` | Administrator only |
| Every patient's package | `ExportAllPatientData` | Administrator only |
| One patient's readable record on its own | `ExportPatientReadableRecord` | Administrator only |

All three are ordinary (not protected) permissions: an Administrator can grant any of them to another role through the permission policy screens, with no code change. They are separate: holding one does not open another. The history list and the overview are open to anyone who holds any of the three; the history shows a person only their own exports unless they hold `ExportAllPatientData`.

### The downloads

Every download is a `POST` with a CSRF token (every export writes a history row and an audit entry, and a bulk one is expensive, so a plain link must not be able to start one). An optional JSON body `{ "includeDocumentFiles": true, "folderNaming": "IdOnly" | "NameAndId" }` chooses the options; no body means document files included and the practice's folder naming.

| Route | Permission | Answer |
|---|---|---|
| `POST api/patient-exports/patients/{id}/package` | `ExportPatientData` | `alveara-patient-package-<exportId>.zip` |
| `POST api/patient-exports/all-patients/package` | `ExportAllPatientData` | `alveara-all-patients-package-<exportId>.zip` |
| `POST api/patient-exports/patients/{id}/readable-record` | `ExportPatientReadableRecord` | `alveara-readable-record-<exportId>.html` |
| `GET api/patient-exports/history?limit=&before=` | any of the three | the history, newest first (limit 1 to 200, default 50) |
| `GET api/patient-exports/overview` | any of the three | what the screen needs before an export starts |

Downloads are sent with `Cache-Control: no-store` and `X-Content-Type-Options: nosniff`, with `X-Export-Id` and `X-Export-Result` (`Complete` or `Partial`); a package also carries `X-Export-Problems` and `X-Package-Sha256`. The readable record is an attachment with a `Content-Security-Policy` that includes `sandbox`. The temporary file a package is built in is deleted when the response ends, sent or abandoned. Failures return the stable `error` code the screen maps to words: `patient_not_found` (404), `patient_merged` (409), `too_many_patients` and `package_too_large` (413), `export_in_progress` (409), `permission_revoked` (403), `permission_policy_unavailable` (503), `invalid_folder_naming` (400), `invalid_limit` (400), and `export_failed` (500, nothing was delivered).

### What is in a package

```
README.txt                                  what this is, what is in it and what is not, how it was read
manifest.json                               written last; a SHA-256 of every other file
data/<domain>.json                          one file per domain (19 of them)
readable-record.html                        one patient's package: the record as a person reads it
readable-records/<patient folder>.html      an all-patients package: one page per patient
documents/<patient folder>/<document id>/<file name>     the document files, when included
```

The 19 domains are registration (with its history of changes), appointments, forms, visits and clinical notes, medical history, safety alerts, the dental chart, diagnoses, periodontal charting, treatment plans, completed procedures, billing, prescriptions, documents, messages, follow-up tasks, the recall plan, referrals and lab cases, and the merge and unmerge history. Each `data/<domain>.json` holds the rows of its tables exactly as the system stores them: each column under its database name, GUIDs and dates as text (a date without a time as `yyyy-MM-dd`, a time as ISO 8601), binary as base64, `null` as `null`, rows in primary-key order so the same data gives the same file. Rows that records refer to (a procedure definition, an operatory) are under `lookupTables`, so relationships still resolve inside the package.

**Left out on purpose** (each with its reason in the manifest and on the screen): sign-in accounts and staff identities, credentials and one-time codes (a staff member appears in a record by display name only); the access policy; the audit log (disclosures are reported by the export history); practice-wide configuration; and technical bookkeeping. Nothing from these is in any file; an arrangement test fails if a table is added to the database without being placed in a domain, a lookup or an excluded group.

### The manifest (`manifest.json`, format `alveara.patient-data-package` version 1)

| Member | Meaning |
|---|---|
| `package`, `schemaVersion`, `appVersion`, `exportId`, `generatedAtUtc`, `generatedByUserId` | what it is and where it came from (`schemaVersion` is the last applied migration; times are UTC) |
| `scope` | `kind` (`OnePatient` or `AllPatients`), `patientCount`, `subjectRecordId` (null for all patients) |
| `result` | `Complete` or `Partial`; Partial means `problems` is not empty |
| `domains` | each domain: `status` (`Included` or `Unavailable`), file, bytes, hash, and the rows of each of its tables |
| `excluded` | each excluded group with its reason and tables |
| `files` | path, bytes and SHA-256 of every file in the package except the manifest |
| `readableRecord`, `readableRecords` | where the readable page(s) are |
| `documents` | every document: `status`, original `fileName`, the recorded `sizeBytes` and `sha256`, the hash found at export (`actualSha256`), and its `path` |
| `patientFolders`, `folderNaming`, `documentFilesIncluded` | which folder holds which patient, how folders were named, and whether files travel |
| `readConsistency` | `Snapshot` or `TableByTable` (see below) |
| `problems` | each with a stable `code` (`domain_unavailable`, `readable_record_unavailable`, `document_missing`, `document_hash_mismatch`, `document_unreadable`), the part it concerns and a plain message, never a value from a record |
| `fhir` | reserved, always null today (no standard-format copy is produced) |

**Document statuses:** `Included` (the file is in the package and matches the hash recorded when it was imported), `HashMismatch` (it is in the package as found but differs from what was imported: do not rely on it; the package is Partial), `Missing` (not in storage; no file; Partial), `Unreadable` (could not be read just now; Partial) and `NotIncluded` (the export was made without document files; the document is still listed with its recorded hash; not a problem).

### Folder names

Patient folders are named by the patient's id (the default: nothing about the patient shows in a file listing) or, when chosen, `Last-First_<id>`. A name keeps letters and digits (accents included) and becomes single hyphens; every other character is dropped; it is cut at 40 characters; the id always stays on the end, so two patients with one name never share a folder. A document's file name loses any folder part, reserved character, device name (`CON`, `LPT1`, ...) and trailing dot, so a name someone typed can never leave its folder. The choice is per export (the screen warns that names then appear in file listings, backup logs and email previews) or a practice default (`DataExport:DefaultFolderNaming`). `manifest.json` maps every patient id to its folder, so no reader parses folder names.

### The readable record

A page that opens in any browser and prints cleanly: the registration summary, medical history, safety alerts, clearances, the dental chart, diagnoses, treatment plans, completed procedures, visits with notes and vitals, prescriptions, appointments, billing as totals by kind (never a balance), documents with their SHA-256, and compact periodontal, forms, follow-up, referral and lab and recall sections. A section with nothing says "None recorded." so an empty section is never mistaken for a missing one. It has no script, no outside file and no image, a content-security policy that forbids all of them, and every value encoded. Times are in the practice time zone with their UTC offset; the machine data stays in UTC.

It is one function (`ReadableRecordLoader` and `ReadableRecordHtml`) used two ways: inside the package (it lists the documents and says whether the files travel with it) and **stand-alone** from the "Download readable record" button on a patient's page and on the Export screen, with its own permission, history row and audit event. The stand-alone page lists documents by name and says their files are only in the complete package.

### Reading the database

An all-patients export saves the ids of everyone who has not been merged away in a temporary table on its own connection, so every table and every page uses the same patients even if one is registered while it runs. Only one all-patients export runs at a time (a second is refused with `export_in_progress`, not queued). When the database option `ALLOW_SNAPSHOT_ISOLATION` is on, the whole read happens in one snapshot transaction and the manifest says `Snapshot`; when it is off the tables are read one after another, the manifest and README say `TableByTable`, and a change made while the export runs may show in some tables and not in others (the manifest time says when it started). The export works either way. See `docs/operations/DATABASE_OPTIONS.md`.

### Limits and settings

`DataExport` section of the configuration; every value has a safe default.

| Setting | Default | Meaning |
|---|---|---|
| `MaxPatients` | 5000 | more patients than this: refused (413 `too_many_patients`), never cut short |
| `MaxPackageBytes` | 2 GB | the recorded size of the document files is checked before anything is built, and the package size after every domain: over the limit, refused (413 `package_too_large`) |
| `TempDirectory` | the system temporary folder | where a package is assembled; every file there is deleted when closed, on success and on failure |
| `ReadTimeoutSeconds` | 120 | an explicit time limit on every database read |
| `DefaultFolderNaming` | `IdOnly` | `IdOnly` or `NameAndId`; any other value is refused |
| `InterruptedAfterMinutes` | 240 | an export still marked Started this long after it began is shown as interrupted |

### The history and the log

Every export, finished or not, has a row in `DataExportHistory`, written as `Started` **before** any data is read and finished once: who, when, package or readable record, one patient or all, whether document files were included, the result (`Started`, `Complete`, `Partial`, `Failed`), the patient and file counts, the size, the problem count and the SHA-256 of the file that was sent. The database refuses to delete a row, to change what it recorded, or to change a finished row. A row never holds exported content: no name, no contact detail, no value from a record. One-patient lines carry the patient as an id; the screen links it to the record for people who may open records.

The audit entry is written in the **same save** as the row's finish, before anything is sent: `PatientDataPackageExported`, `AllPatientDataPackageExported` or `PatientReadableRecordExported` (the user, the time, the patient or the count, the export id and sizes; no name). If it cannot be saved nothing is sent and the row is `Failed`; a failed export also writes `DataExportFailed` (the export id and the error class, never the message). The history means the server produced the package and handed it to the response; it cannot prove the browser received it.

An all-patients export also asks again, from a fresh scope that reads the account and the permission policy anew, whether the person may still do it: before anything is read, after every domain, every 250 patients while the pages are made, and just before delivery. A permission withdrawn part way, a disabled account or a policy that cannot be read stops it (403 `permission_revoked` or 503) and nothing is delivered.

Repeating an export makes another export with its own id and row. The data files of two exports of unchanged data are identical; the readable page differs in the time it was made and the export id.

### The Export patient data screen

Under the admin menu, for anyone who holds any of the three permissions: one patient or all patients, a complete package or the readable record alone (each only if you hold its permission), document files on or off, folder naming (with the warning about names), the patient and document counts against the limits (the button is disabled with the reason when over a limit), how an all-patients export would read the database, and what a package holds and leaves out with the reason for each exclusion. While an export runs the person can cancel (the server stops and the row is `Failed`); afterwards the screen says what was saved and that it was logged, and says plainly when a package is **Partial** and where to look (`manifest.json`, the `problems` list). The history table shows each export's result in words (Complete, Partial, Failed, In progress, Interrupted), size and a short file check, in the practice time zone.

### Failure modes the package handles and does not

Handled: a domain that cannot be read (listed as `Unavailable`, the package is Partial, no half-written file); a document file that is missing, changed or unreadable (listed, Partial); too many patients or too large a package (refused, nothing left behind); a second all-patients export (refused); a permission withdrawn during an export (stopped, nothing delivered); a cancelled or interrupted request (every temporary file deleted, the row Failed); an audit store that is down (nothing sent); a merged-away patient (409) and an unknown one (404); a hostile patient or file name (cannot leave its folder); a full disk (the export fails, it is not blamed on a document).

Not handled: **the package is not encrypted**, it holds personal health information, and whoever holds the file holds the data (the README says so; keep it where only authorised people can reach it); a standard-format (FHIR) copy; importing a package back (not built: ALV-018-C02 imports CSV files of patients, appointments and opening balances, see `docs/BULK_IMPORT.md`); progress while an export runs (one request; a clinic-sized export takes seconds, see the walkthrough numbers, but a proxy with a short time limit could cut a large one off); a document file changed in storage between the check and the copy (the hash recorded is of the bytes copied, so the manifest stays truthful); a database that does not allow snapshots (read table by table, said so in the manifest); and one patient's readable page failing while others succeed is handled in code (that patient is listed as a problem, the rest continue) but has no automated test.
