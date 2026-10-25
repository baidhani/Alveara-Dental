# Duplicate patient records and the controlled merge (STORY-017, ALV-017-C01)

Requirement: REQ-018. Owner: the system administrator. Built on STORY-014 (the patient-owned data it moves). STORY-017 built the scan, the merge and its screen; ALV-017-C01 made it safe to live with: a server-side confirmation, an identity check, a review queue with dismissals, a guard on retired records, the lineage on the patient's own page, and an undo.

## What it does

1. **Finds** pairs of patients who may be the same person (`PatientDuplicateScanner`). Every rule needs the **same date of birth**, because a shared surname, phone or email alone is a normal family. The rules are the registration check's own (a parity test keeps them equal): the same name; the same last name; the same first name and a last name one or two typos away; the same phone number (at least seven digits); the same email in any letter case. Each pair gets a score (the weights add up and stop at 100), the reasons in words, a suggested record to keep (the active one, else the older) and a flag when both are in one household (twins). It only reads. It shows at most 200 pairs, strongest first, and says when it cut the list.
2. **Previews** a merge (`PatientMergeService.PreviewAsync`): the fields the two records fill in differently, the fields the kept record had empty, how many rows would move from which table, which records would stay with the retired record, and anything that stops the merge. It changes nothing.
3. **Merges** (`MergeAsync`), in one transaction or not at all. The record the administrator chooses to keep carries on; the other is **retired: it stays in place, marked merged and inactive, and is never deleted**.

Who: `MergePatients`, held by the administrator only by default (an administrator can grant it on purpose; it is not in the protected admin-only set). Every endpoint below needs it; every other role is refused (403) and an anonymous caller gets 401.

## What ALV-017-C01 adds

* **No merge without explicit confirmation, enforced by the server.** A request must carry `confirmed: true`; a request without it is `confirmation_required` (400) and nothing happens, whatever the screen did.
* **Conflicting identities need their own confirmation.** If the two records differ in date of birth, in last name, or in a first name more than two typos apart (a nickname), the preview lists the differences (`identityConflicts`), the screen warns, and the merge is `identity_conflict` (409) unless the request also carries `confirmIdentityDifference: true`. A genuine duplicate has none. The audit text records which differences were confirmed on purpose.
* **A review queue with decisions.** An administrator can dismiss a suggested pair as *not a duplicate* with a reason (twins, relatives). The pair leaves the list and is counted, the decision is a row of its own (who, when, why; never deleted or edited; a restore is a second step that records who and when), the same pair is one pair in either order, and two audit entries are written for each change. Restoring brings the pair back; dismissing it again is a new decision.
* **A retired record takes no new records.** The merge-scope trigger of every table that owns a patient id also refuses, outside a merge, a new row naming a patient whose record was merged away (error 51235; the answer to a caller is a plain 409 `patient_merged`). Existing rows of the retired record can still change (the check is for *new* records only). Most screens already refuse an inactive patient with their own message; this is the backstop behind them.
* **The lineage on the patient's own page.** `GET /api/patients/{id}/merge-history` (anyone who may view patients) says which record this one was merged into, or which records were merged into it, with when, by whom and why. A retired record's page says so in words and links to the record that carries on; a surviving record lists what was merged into it. If the history cannot be read the page says so instead of staying silent.
* **Undo, where practical and safe.** A merge now keeps a ledger of the exact rows it moved (each by the key it has afterwards). *Undo* moves back exactly those rows, puts back a field value only while the surviving record still holds the value the merge put there (an edit made since is kept and said so), clears a household the merge carried over if unchanged, makes the retired record active again, and records who, when and why with two audit entries. It happens in one transaction and the original lineage stays as history. The review is a real dry run: the rows are moved and every record is checked inside a transaction that is rolled back, so what the preview promises is what the undo does.

### When an undo is refused (nothing changes)

| Code | Why |
|---|---|
| `unmerge_unavailable` | the merge was made before merges kept a ledger (recovery is by restoring a backup) |
| `already_unmerged`, `merge_not_held` | it was undone already, or no longer holds |
| `survivor_merged` | the surviving record was itself merged into another: undo that merge first |
| `unmerge_blocked` | records made after the merge depend on rows that would move back (for example a link between a retired-chart row and a new survivor-chart row); the message names the table |
| `confirmation_required`, `reason_invalid`, `idempotency_key_invalid`, `row_version_*`, `patient_changed` | the same rules as a merge |

Not restored by design: a guarantor link from the surviving record to the retired one (the merge cleared it so nobody is their own guarantor), and any field edited since. After an undo the same record can be merged away again later (one merge at a time per record, enforced by the database).

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
| `GET /api/patient-merges/log` | The latest merges: who, when, why, between which records, how much moved or stayed, and whether each was undone (`undoneAtUtc`, `undoneByName`, `canUndo`). |
| `GET/POST /api/patient-merges/dismissals`, `POST .../dismissals/{id}/restore` | The dismissals in force; dismiss a pair (`firstPatientId`, `secondPatientId`, `reason`); restore one. |
| `GET /api/patient-merges/{mergeId}/unmerge-preview` | What an undo would do (a dry run that changes nothing). |
| `POST /api/patient-merges/{mergeId}/unmerge` | The undo. Needs a CSRF token, an `Idempotency-Key`, `reason`, `confirmed: true` and the two row versions the preview showed. |
| `GET /api/patients/{id}/merge-history` | The patient's place in the merge lineage (needs `ViewPatientRecords`). |

Stable error codes: `choice_required`, `choice_invalid`, `patient_changed`, `already_merged`, `survivor_merged`, `merge_blocked`, `merge_inconsistent`, `merge_conflict`, `key_reused`, `reason_invalid`, `idempotency_key_invalid`, `row_version_required`, `row_version_invalid`, `same_patient`, `patient_not_found`, `patient_merged`, `confirmation_required`, `identity_conflict`, `dismissal_not_found`, `unmerge_unavailable`, `unmerge_blocked`, `already_unmerged`, `merge_not_held`, `merge_not_found`.

The screen is `/admin/patient-merge` ("Duplicate patients").

## Failure-first answers

1. **What if it fails?** Nothing is merged; the transaction rolls back, the audit write included.
2. **Retry?** The caller retries with the same Idempotency-Key; the answer is the merge already made, or a fresh attempt if none was made. There is no automatic retry inside the service.
3. **Recovery when it keeps failing?** The log and the lineage show what happened; `merge_inconsistent` means the data was not in the shape the registry expects and needs a person to look.
4. **Handled / not handled.** Handled: stale records, repeats, simultaneous requests, a failing audit store, a record that cannot move, a merge the data cannot support. Not handled: a merge made before ledgers existed cannot be undone (recovery is by restoring a backup); an undo is refused rather than forced when newer records depend on the moved rows; a guarantor link the merge cleared is not restored; a collision with dependants that point at something that must move blocks the merge instead of splitting it. (Retired records now say so on their own page and take no new records; undo exists.)

## Verification

`PatientOwnedDataTests`, `PatientDuplicateScannerTests`, `PatientMergeSchemaTests` (against a real SQL Server, rows written around any service), `PatientMergeServiceTests` (a real chart: every registered column conserved), `PatientMergeApiTests`, `PatientMergeBackupRestoreTests` (a real backup and restore), the screen tests `PatientMergeScreens.test.tsx`, and the real-backend walkthrough `e2e/patient-merge-real-backend.spec.ts` (run with `scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.patient-merge.config.ts -OutputVariable PATIENT_MERGE_E2E_OUT`); evidence in `docs/testing/patient-merge-walkthrough/`. ALV-017-C01 adds `PatientDuplicateReviewTests` (dismissals, against a real SQL Server), `RetiredRecordGuardTests`, `PatientUnmergeSchemaTests` (the ledger, the undo record and the narrow way a merged record comes back), `MergeChartTestBase` with `PatientMergeChartPreservationTests` (one real chart across appointments, notes and history, odontogram, perio, diagnoses, plans, completed procedures, billing with a payment, prescriptions, documents, household and guarantor, referrals and lab cases, tasks and audit references: every registered column conserved and every record still agreeing), `PatientMergeRecoveryTests` (faults planted at four points of a merge) and `PatientUnmergeServiceTests` (the round trip, refusals, newer dependants, faults at four points of an undo), the restore proof extended (a restored database holds the ledger and the dismissals and can undo the merge), and the walkthrough extended (dismiss and restore, the banner on a retired record, the undo).
