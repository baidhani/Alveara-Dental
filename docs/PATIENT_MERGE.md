# Duplicate patient records and the controlled merge (STORY-017)

Requirement: REQ-018. Owner: the system administrator. Built on STORY-014 (the patient-owned data it moves).

## What it does

1. **Finds** pairs of patients who may be the same person (`PatientDuplicateScanner`). Every rule needs the **same date of birth**, because a shared surname, phone or email alone is a normal family. The rules are the registration check's own (a parity test keeps them equal): the same name; the same last name; the same first name and a last name one or two typos away; the same phone number (at least seven digits); the same email in any letter case. Each pair gets a score (the weights add up and stop at 100), the reasons in words, a suggested record to keep (the active one, else the older) and a flag when both are in one household (twins). It only reads. It shows at most 200 pairs, strongest first, and says when it cut the list.
2. **Previews** a merge (`PatientMergeService.PreviewAsync`): the fields the two records fill in differently, the fields the kept record had empty, how many rows would move from which table, which records would stay with the retired record, and anything that stops the merge. It changes nothing.
3. **Merges** (`MergeAsync`), in one transaction or not at all. The record the administrator chooses to keep carries on; the other is **retired: it stays in place, marked merged and inactive, and is never deleted**.

Who: `MergePatients`, held by the administrator only by default (an administrator can grant it on purpose; it is not in the protected admin-only set).

## What a merge does, in order

| Step | What | If it fails |
|---|---|---|
| Lock | Locks both patient rows. | Waits; a second request with the same key then finds the first merge and answers with it. |
| Check | Both records exist and neither is already merged; the record versions the administrator reviewed are still current; a reason and an Idempotency-Key are present. | 400 / 404 / 409 with a stable code; nothing is changed. |
| Plan | Works out what moves, what stays, the answers needed. | `merge_blocked` (409) or `choice_required` (400); nothing is changed. |
| Move | Sets the session value `alveara.patient_merge` and changes the patient id, and nothing else, of every row in every column `PatientOwnedData` lists (56 columns in 54 tables). | Any error rolls the transaction back. |
| Verify | No row of the retired patient is left except those meant to stay; every row that points at another row of the chart still agrees with it about whose chart it is. | `merge_inconsistent` (500); everything rolled back. |
| Record | Applies the answers to the kept record, writes the lineage (`PatientMerges`, `PatientMergeMoves`, `PatientMergeResolutions`), history rows, two audit entries (one for each record) with the administrator's id and the time, and marks the retired patient merged. | Rolled back with everything else, the audit write included. |

## Conflicts and how linked data is preserved

* **A field both records fill in with different values** (phone, email, address lines, city, state, postal code, middle name, sex): the administrator answers for each, *keep the kept record's* or *take the retired record's*. The value that was replaced is kept in the lineage, so no value is lost. A field the kept record had empty is filled from the retired record and recorded. Name and date of birth are not reconcilable: together they make the unique identity key, so the kept record keeps its own.
* **Household**: carried over when the kept record has none. **Guarantor**: other patients whose guarantor was the retired record now have the kept record; if the kept record's own guarantor was the retired record it is cleared, so no one is their own guarantor.
* **A record the kept patient already has its one of** (a billing account, a recall plan, a clinical item or safety alert of the same name, a draft perio session, a plan with the same reference code, and so on; the rules are read from the database's own unique indexes, filters included): it cannot move. It **stays with the retired patient together with everything that depends on it** (its history, its acknowledgements, the rows that point at it), reachable through the lineage, and is recorded as `LeftWithSource`.
* **A merge the data cannot support**: if a record that has to stay points at one that would move, splitting a record between two patients would be worse than refusing, so the merge is blocked (`merge_blocked`) and says which tables. Nothing is changed.

## How the database allows it (and nothing else)

Many tables are append-only or final by design (61 `INSTEAD OF` triggers and many more `AFTER` triggers refuse updates). The merge needs exactly one exception, and it is narrow:

* Inside a transaction that has set the session value `alveara.patient_merge` to 1, those triggers stand aside for an UPDATE (an `INSTEAD OF` trigger performs the patient-id change itself).
* A `TR_<table>_MergeScope` trigger on each of the 54 tables then refuses that UPDATE unless it sets nothing but the patient-id columns, and refuses any insert or delete (error 51234).
* Everywhere else every table refuses exactly as before. A test reads the catalog and fails if a table lacks its merge-scope trigger or any update trigger lacks the line.
* The lineage is append-only (51230); a merged-away patient cannot be the survivor of another merge (51231); a patient is marked merged only by a lineage with the same time (51232) and then never changes (51233). Editing a retired record through the API is a 409 `patient_merged`.
* `PatientOwnedData` lists every column that holds a patient id, and `PatientOwnedDataTests` fails when the model and the list disagree, so a new table cannot be forgotten.

## API

All behind `MergePatients`; writes need a CSRF token.

| | |
|---|---|
| `GET /api/patient-merges/duplicates` | The suggested pairs. |
| `GET /api/patient-merges/preview?sourceId=&survivorId=` | What the merge would do. `sourceId` is the record to retire. |
| `POST /api/patient-merges` | The merge. Needs an `Idempotency-Key`, a reason (up to 500 characters), the two row versions the preview showed, and `fieldChoices` (field name to `Survivor` or `Source`). |
| `GET /api/patient-merges/log` | The latest merges: who, when, why, between which records, how much moved or stayed. |

Stable error codes: `choice_required`, `choice_invalid`, `patient_changed`, `already_merged`, `survivor_merged`, `merge_blocked`, `merge_inconsistent`, `merge_conflict`, `key_reused`, `reason_invalid`, `idempotency_key_invalid`, `row_version_required`, `row_version_invalid`, `same_patient`, `patient_not_found`, `patient_merged`.

The screen is `/admin/patient-merge` ("Duplicate patients").

## Failure-first answers

1. **What if it fails?** Nothing is merged; the transaction rolls back, the audit write included.
2. **Retry?** The caller retries with the same Idempotency-Key; the answer is the merge already made, or a fresh attempt if none was made. There is no automatic retry inside the service.
3. **Recovery when it keeps failing?** The log and the lineage show what happened; `merge_inconsistent` means the data was not in the shape the registry expects and needs a person to look.
4. **Handled / not handled.** Handled: stale records, repeats, simultaneous requests, a failing audit store, a record that cannot move, a merge the data cannot support. Not handled: a merge cannot be undone from the screen (the lineage keeps the evidence to build that); a retired record is not yet shown as "merged into X" on its own page; other screens do not yet refuse to attach *new* work to a retired (inactive) record; a collision with dependants that point at something that must move blocks the merge instead of splitting it.

## Verification

`PatientOwnedDataTests`, `PatientDuplicateScannerTests`, `PatientMergeSchemaTests` (against a real SQL Server, rows written around any service), `PatientMergeServiceTests` (a real chart: every registered column conserved), `PatientMergeApiTests`, the screen tests `PatientMergeScreens.test.tsx`, and the real-backend walkthrough `e2e/patient-merge-real-backend.spec.ts` (run with `scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.patient-merge.config.ts -OutputVariable PATIENT_MERGE_E2E_OUT`); evidence in `docs/testing/patient-merge-walkthrough/`. Dismissing a pair as "not a duplicate" and an undo are planned for ALV-017-C01.
