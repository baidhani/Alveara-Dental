# Patient data portability (STORY-018)

**Status: built and verified for STORY-018.** The export (`GET api/patients/{id}/export`, the "Export patient file" button on a patient's page) and the import (`POST api/patients/import`, the "Import a patient" page under the admin menu) are in the system; the walkthrough screenshots are in `docs/testing/patient-portability-walkthrough/`.

A practice must be able to take one patient's record out of Alveara in a format other systems and people can read, and to bring such a record in, without silently accepting a bad file. This slice covers one patient's registration data. The whole-chart export package (clinical, financial and document data, with a manifest and file checks) is a later companion story (ALV-018-C01), and bulk import with a migration workbench is another (ALV-018-C02).

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
- **Not in the file:** user account data, passwords or keys, internal row versions, household and guarantor links (they point at other records in this database), and clinical, financial and document data (ALV-018-C01). A patient who was merged away is not exported (the answer names the record that carries on).

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
- **Likely duplicate** (the existing duplicate detector's suggestions): refused (409 `possible_duplicate`) with the candidates. This slice never creates a record over a likely duplicate; the person registers it from the form, where each candidate is acknowledged (bulk acknowledgement belongs to ALV-018-C02).
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
