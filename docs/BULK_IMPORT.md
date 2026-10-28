# Bulk import and migration (ALV-018-C02)

**Status: built; Stage 1 evidence in `.alveara/handoffs/ALV-018-C02/`.** The Data import screens (`/admin/data-import`), the endpoints under `api/import-batches` and the rules below are in the system. This is separate from STORY-018's one-patient file import (`docs/PATIENT_PORTABILITY.md`), which is unchanged.

A practice moving from another system needs to bring in its patients, its future appointments and the amounts its patients owe, without silently corrupting who a patient is, what the schedule says or what the ledger says. The workbench takes **one CSV file at a time**, checks the whole file, shows what would happen (a **dry-run**, which writes nothing), lets a person settle the doubtful rows, and only then writes. Everything it does is logged, and a finished batch can be reconciled against the records it made.

## Who may do it

| Action | Permission | Held by default |
|---|---|---|
| Use the Data import screens and every `api/import-batches` call | `BulkImportData` | Administrator only |
| Commit or retry a batch of **opening balances** (as well as the above) | `ManageBilling` | Administrator, Billing and Office Manager |
| Open the patient records a duplicate decision compares | `ViewPatientRecords` | most clinical and front-desk roles (see the permission matrix) |

`BulkImportData` is an ordinary (not protected) permission: an Administrator can grant it through the permission policy screens. It is separate from `ImportPatientData` (STORY-018). A balance batch writes to patients' billing ledgers, so `BulkImportData` alone is not enough: without `ManageBilling` the commit and the retry are refused (403 `billing_permission_required`) before anything changes. Without `ViewPatientRecords` the duplicate queue is read-only, because deciding without looking is the mistake the queue exists to prevent.

## The flow

1. **Send the file** (kind, a label for where it came from, the CSV). The whole file is read as untrusted text first. Any problem refuses the whole file, stores nothing and lists every problem with its row and column name (never the value). A file sent twice (same kind, same content) returns the batch that is already waiting.
2. **Match the columns.** The page suggests a field for each column by its name; the person confirms. The server checks the mapping against the file's own header and the practice's rules (`unknown_column`, `unknown_field`, `field_mapped_twice`, `required_field_unmapped`). Saving a new mapping makes an earlier dry-run stale.
3. **Dry-run.** Every row is judged exactly as the commit will judge it. Nothing is written. The result for each row is **Valid**, **Rejected** (something wrong with its values), **Unresolved** (a person must decide) or **Skipped** (nothing to do), with a reason code and, for a rejected row, a fixed message for each field. It can be run again.
4. **Decide the likely duplicates** (patient files). See below.
5. **Commit.** The person chooses the policy, ticks a confirmation and the page sends the number of valid rows it showed. If the results changed in the meantime the server refuses (409 `results_changed`) and the page reloads. The work runs on the durable background queue and the page follows it.
6. **Reconcile.** The page shows what happened, counted from the database.

## File kinds

All files are UTF-8 CSV (a byte-order mark is accepted), comma separated, the first row the column names. A field may be quoted and then may hold commas, line breaks and doubled quotes. Limits (configurable in the `DataImport` section; all have defaults): 10 MB, 50,000 data rows, 60 columns, 2,000 characters in a cell. A row with more or fewer cells than the header is refused rather than padded, because a shifted column would put a value in the wrong field. Problem codes: `file_empty`, `file_too_large`, `not_text`, `control_character`, `unterminated_quote`, `stray_quote`, `row_width`, `cell_too_long`, `too_many_rows`, `no_data_rows`, and header problems.

### Patients
Fields: first name, middle name, last name, date of birth (`yyyy-MM-dd`), sex, phone, email, address, address line 2, city, state, postal code. Which are required follows the practice's own registration settings. Each row is validated by the same rules and registered by the same step as the front desk's registration form, under the key `import-{batch}-{row}`.

**Duplicates are never merged.** A row that is exactly a registered patient (name and date of birth) is Skipped (`already_registered`); an earlier row of the same file that is the same person makes the later one Skipped (`duplicate_in_file`); a row that only looks like a registered patient is **Unresolved** (`possible_duplicate`) and waits in the queue. For each, a person opens the matched records and decides **"different person"** (the row becomes Valid and is registered) or **"same person"** (the row is Skipped). The decision is recorded with who and when and survives a repeated dry-run. The commit checks again, so a person registered between the dry-run and the commit is caught.

### Appointments
Fields: patient first name, last name and date of birth; provider, operatory and appointment type (by name, ignoring capitals and spacing); start (`yyyy-MM-dd HH:mm`, practice time); duration in minutes (optional: the type's own); notes. **Future appointments only** (a start in the past is `start_in_past`). The patient must already be registered and is found by first name, last name and date of birth exactly; no match, more than one match, a merged-away patient or an inactive patient rejects the row with a fixed message. Each row goes through the **scheduling conflict service**, the same one the Calendar uses, so a provider, operatory or patient clash, or a provider who is not working, rejects the row with the scheduler's own code (`provider_double_booked`, `operatory_conflict`, `patient_double_booked`, `provider_unavailable`) and is reported, never double-booked. An identical appointment already on the schedule makes the row Skipped (`already_scheduled`). Conflicts are **rejected and reported only**; resolving them is not part of this tool.

### Opening balances
Fields: patient first name, last name and date of birth; amount owed; as-of date (optional); currency (optional, USD only). The amount must be more than zero, in dollars and cents, within the ledger's ceiling, with no currency sign or thousands separator; a credit is refused with its own message. One balance for each patient in a file (`patient_repeated_in_file`), and only for a patient whose ledger has **no entry at all** (`account_not_empty`): an opening balance is never added to a history and never overwrites a balance. A committed balance is an **Adjustment appended to the ledger as that patient's first entry**, with the fixed reason "Opening balance migrated from an import.", the batch id as its correlation id and the key `import-bal-{batch}-{row}`, so the ledger itself says where it came from and the entry stays historically distinguishable. The ledger stays append-only and the balance stays the sum of the entries; nothing sets a total. The dry-run shows the **total of the valid balances** so the person can check it against the old system. The as-of date is checked but not stored on the entry.

## The batch policy (visible before the commit)

| Policy | What it does | Available for |
|---|---|---|
| **All or nothing** (default where offered) | One transaction for the whole file. If any row stops being valid at commit time, nothing is written and the batch goes back to needing a dry-run (outcome `Refused`). | Patients, when every row is valid or skipped and there are at most 10,000 valid rows |
| **Commit the valid rows only** | Rows are committed in windows of 500 (one transaction for each window; appointments and balances one row at a time). Rows that are not valid stay in their lists with their reasons. | All kinds |

Appointments and balances are recorded one at a time, each in its own transaction (the scheduler and the ledger have their own transactions and locks), so all-or-nothing is refused for them (`policy_not_supported`) and the screen says why.

## Interruption, retry and idempotency

Every effect is keyed, so doing it twice does nothing the second time: a patient under `import-{batch}-{row}`, an appointment under `import-appt-{batch}-{row}`, a ledger entry under `import-bal-{batch}-{row}`. A crash between writing a record and saving its row simply repeats and finds the record. A job that fails is retried up to three times and then marked failed; the batch's finish step then reports it **Interrupted** (what was done stays done) and the commit can be confirmed again.

A batch can also get **stuck**: Committing with no job of its round waiting or running (a failed finish job, jobs lost between the save and the queue). The batch view then says `canRetry` and the page offers **Retry the commit**, which starts a new round of fresh jobs. Rows already done are never repeated. A retry of a batch that is not stuck is refused (409 `not_stuck`); two people retrying at once is refused for one of them (`batch_changed`). The person who retries becomes the confirmer, which is why a balance retry also needs `ManageBilling`.

## Reconciliation, the error report and the sweep

- **Reconciliation** (`GET {id}/reconciliation`): rows counted by status and reason from the database; the records the batch really made (found by their keys) against the rows it says it accepted; for a finished batch the stored counters; for balances the batch's own total against what the ledger holds for the batch. Each difference is a named problem (`row_count_mismatch`, `accepted_result_missing`, `unexplained_records`, `valid_rows_left`, `counters_mismatch`, `amount_mismatch`) and `reconciled` is true only when there are none.
- **Error report** (`GET {id}/error-report`): a CSV (with a byte-order mark so a spreadsheet reads it correctly) of every Rejected, Unresolved and Skipped row: row, status, reason, field, message, duplicate-of row. It holds only the fixed messages, never a value from the file, and a cell a spreadsheet could read as a formula is neutralised.
- **Stray-file sweep** (`POST orphan-sweep`): the uploaded file lives in its own `imports` folder inside the storage root, not in the document store (whose integrity scan would remove a file no document refers to). If the server stopped between storing a file and saving its batch, that file is left behind; the sweep removes files no batch refers to that are older than 24 hours (`DataImport:OrphanFileGraceHours`), re-checking each just before the delete, and reports open batches whose file is missing. A batch's own file is never touched. It is safe to repeat.

## Endpoints (`api/import-batches`, all `BulkImportData`, responses `no-store`)

`POST ?sourceKind=&label=` (the CSV is the body; CSRF), `GET` (list), `GET {id}`, `GET {id}/mapping-suggestion`, `PUT {id}/mapping`, `POST {id}/dry-run`, `GET {id}/rows?status=&after=&limit=`, `POST {id}/rows/{row}/decision`, `POST {id}/commit` (202), `POST {id}/retry` (202), `GET {id}/reconciliation`, `GET {id}/error-report`, `POST orphan-sweep`. Every refusal carries a stable `error` code the screens turn into words.

## The log

Audit events: `ImportBatchCreated`, `ImportFileRejected`, `ImportMappingSaved`, `ImportDryRunCompleted`, `ImportRowDecided`, `ImportCommitStarted`, `ImportCommitCompleted`, `ImportCommitRefused`, `ImportCommitRetried`, `ImportFilesSwept`, and the ledger's `BillingOpeningBalanceRecorded`. They carry the actor, ids, counts, sizes, hashes and codes, and **no name, date of birth, contact detail or other value from a row** of the file; the same holds for logs and for every message the screens show. The only amounts are the ones every ledger entry's audit already carries (the entry's amount and currency) and a balance batch's total.

## Data model

`ImportBatches` (one per file: kind, label, status, policy, file hash and size, counters, mapping, confirmer, outcome, dry-run and balance totals), `ImportBatchRows` (one per data row: status, reason, fixed field messages, matched patient ids, decision, and the id of the record the row produced). Neither holds a value from the file. Database triggers refuse to delete a batch, to change what it recorded about its file, to change a finished batch or its rows, and to delete a row (errors 51260 to 51268). Migrations: `AddImportBatches`, `AddImportBatchRows`, `AddImportCommit`, `GeneralizeImportRowResult`, `AddImportBatchAmounts`.

## Failure modes handled, and not

Handled: a malformed, oversize or ragged file; unknown, duplicate or unmapped columns; invalid references (patient, provider, operatory, type); a likely duplicate; an appointment clash; an invalid balance or currency; partial batch errors; a process stop during the commit; a retry after an uncertain result; two people acting at once; a stale screen; a replayed job; a deactivated or merged patient; an account used since the dry-run.

Not handled (by design or not yet): appointment conflicts are reported, not resolved; there is no past-appointment import and no clinical or document import; the as-of date of a balance is not stored; the sweep runs only when called (no schedule); an `imports` folder restored from a backup has not been exercised; the rollback guards of three migrations are not tested; a start time that passes between the dry-run and the commit cannot be tested with the real clock.

## Tests and the walkthrough

Backend: `Import*Tests`, `ImportBalance*Tests`, `ImportReportsRetryAndSweepTests` and the API tests in `src/Alveara.Api.Tests`. Client: `src/alveara-client/src/pages/imports/*.test.tsx`. The un-mocked run is `e2e/bulk-import-real-backend.spec.ts` (see `docs/testing/REAL_BACKEND_E2E.md`).
