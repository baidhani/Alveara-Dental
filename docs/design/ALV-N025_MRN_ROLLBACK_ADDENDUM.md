# ALV-N025 — MRN rollback addendum (design change for independent review)

**Status:** proposed, documents only. **No implementation of this change before an independent review approves it** (BRIEFING-R03 stop rule: "Any design change affecting the MRN allocation rule ... requires independent review before implementation of that change").
**Amends:** `ALV-N025_PRE_IMPLEMENTATION_BRIEF.md` (revision 3, approved in BRIEFING-R03, with section 6's packaging clarification of 2026-11-19), sections 5 and 6 and the `MrnMigrationTests` line of section 8. Everything else in the MRN design is unchanged.
**Owner decision (2026-11-19):** option B, "Down archives", with the conditions quoted in section 2.

## 1. The problem

The approved design refuses `AddPatientMrn`'s Down with **51300** once any patient was added after Up, because that patient's number would be lost.

While slice 3 was being written (code not yet run), a survey found that **32 existing migration test classes** (`*SchemaTests`, `*MigrationTests`, `RetiredRecordGuardTests`, `PatientDuplicateReviewTests`, `TestDatabaseTemplateTests` and others) roll the database back to an earlier migration, often after creating patients. Every rollback past `AddPatientMrn` with a patient present would fail with 51300.

The brief planned only an empty-database rollback test, so it did not account for them. Keeping 51300 would make every rollback past this migration brittle, today and for every future migration test.

## 2. Owner conditions (quoted)

- PatientMrnArchive is a durable preservation table, keyed by patient GUID with a unique MRN.
- Down archives every existing (PatientId, MRN) atomically before removing Patients.Mrn.
- If an archive record already exists, it must match exactly; otherwise fail safely rather than overwrite it.
- Up restores archived MRNs first, then generates MRNs only for patients without an archived value.
- The archive must preserve the never-reuse guarantee and must not expose MRNs through ordinary application APIs.
- The addendum must explicitly disclose that a rollback retains this preservation table rather than returning the physical schema to a byte-for-byte pre-migration state.
- Add focused migration tests for down → up restoration, archive conflict refusal, uniqueness, and the existing migration-test rollback pattern.

Option C (drop the numbers when no other data is at risk) was rejected because it weakens "never reused".

## 3. The design

### 3.1 The preservation table

```sql
CREATE TABLE [PatientMrnArchive] (
    [PatientId]   uniqueidentifier NOT NULL CONSTRAINT [PK_PatientMrnArchive] PRIMARY KEY,
    [Mrn]         bigint           NOT NULL,
    [ArchivedAtUtc] datetimeoffset NOT NULL,
    CONSTRAINT [UX_PatientMrnArchive_Mrn] UNIQUE ([Mrn]),
    CONSTRAINT [FK_PatientMrnArchive_Patients] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id])
);
```

- **Keyed by the patient GUID, and each MRN unique** (owner condition).
- **The foreign key** ties every archived number to a patient that exists. Patients are never deleted (brief assumption 4: inactivation and merge keep the row), so an archived number can never be orphaned and later handed to someone else.
- **Append-only (new guard 51302):** an `INSTEAD OF UPDATE, DELETE` trigger, `TR_PatientMrnArchive_Preserve`, refuses any change or removal of an archived row. A row is written once and then only read.
- **Created by Up when it is missing** (`IF OBJECT_ID(...) IS NULL`), and **never dropped by Down**. The table therefore exists before the first Down, and survives every rollback and re-apply.
- **Not in the EF model:** no entity, no `DbSet`, created and read only by the migration's SQL.
  - No application service, API or export can read it.
  - The data package exports named tables from `ExportDomainCatalog`, and this table is not one of them.
  - That is how the "not exposed through ordinary application APIs" condition is met.

### 3.2 Down: archive, then remove

The whole Down runs in the migration's single transaction. A failure at any point leaves the schema and data exactly as before Down.

1. **Conflict check (fail safe, new code 51302).** For every patient with an archive row, the archived MRN must equal the current `Patients.Mrn`. Any mismatch → `THROW 51302` and Down stops, with nothing changed. Under the guards (51298 MRN immutable; the archive append-only) a mismatch means someone altered data outside the system.
2. **Archive** every (PatientId, Mrn) that has no archive row yet, in one `INSERT ... SELECT`. A collision on `UX_PatientMrnArchive_Mrn` (an MRN archived for another patient) is also a conflict. Step 1 checks for it explicitly and refuses with 51302 before the insert is tried.
3. **Remove** guard 51298 (`TR_Patients_MrnImmutable`), the archiving trigger `TR_Patients_ArchiveMrn` (3.4), the unique index `IX_Patients_Mrn` and the `Mrn` column.

Down **no longer throws 51300**. The code stays reserved for "Down refused": brief section 6 still uses it for `AddPatientAccessAudit`'s Down (slice 5). The `alveara.mrn_backfill_rows` extended property of the approved design is not needed and is dropped from the design.

### 3.3 Up: restore, then number the rest

1. Create `PatientMrnArchive` and its guard if they are missing (3.1).
2. Add `Patients.Mrn` (nullable for now).
3. **Restore first:** every patient with an archive row takes its archived MRN, exactly.
4. **Number the rest**, deterministically: the patients without an archive row, ordered by (`CreatedAtUtc`, `Id`).
   - Each takes the UTC seconds of its `CreatedAtUtc`, moved forward past the previous one, and past any number already taken by step 3 or by an archive row.
   - This is a short, ordered loop in the migration's SQL. The approved set-based formula cannot step past restored numbers.
   - On a database with an empty archive it gives the same numbers as the approved backfill.
5. Make `Mrn` NOT NULL, create `IX_Patients_Mrn`, create guard 51298 and the archiving trigger `TR_Patients_ArchiveMrn` (3.4).
6. **Archive the new numbers** from step 4 as well, so the archive always holds every number the database has ever handed out.
   - This makes a later Down → Up give every patient the same number again.
   - It also keeps the archive's MRNs reserved even across further rollbacks.

As in the approved design, the patient triggers (TR_Patients_Merged, 51233) are switched off only for the statements of steps 3–4 and switched back on right after, inside the transaction.

### 3.4 After Up: the never-reuse guarantee at run time

- `PatientMrn` (the save-time allocator) keeps the approved rule: the current second, stepped past the numbers taken.
- "Taken" is now **`Patients.Mrn` and `PatientMrnArchive.Mrn`**: one extra bounded query on the same look-ahead window. A number in the archive is therefore never handed out again, even in the theoretical case where it is not also on a patient.
- **Every new number is archived in the same statement** by an `AFTER INSERT` trigger on `Patients`, `TR_Patients_ArchiveMrn`, created by Up and removed by Down. It inserts (`Id`, `Mrn`, now) into `PatientMrnArchive` for each new patient.
  - It runs in the same transaction as the insert, so a patient never exists without its archive row, and a failed insert leaves neither.
  - Because the table is not in the EF model, the database does this, not the context. The allocator reads the archive with a parameterised SQL query (`SqlQuery<long>`).
  - **Alternative for the reviewer to choose instead:** archive new numbers only at Down time (no `TR_Patients_ArchiveMrn`). That gives a simpler run-time path, but the archive is complete only after a rollback.
  - I recommend archiving on insert: the archive is then always the complete ledger, and Down step 2 is a no-op in normal use.
- The unique index on `Patients.Mrn` and the 60-attempt `+1` retry → `503 mrn_busy` are unchanged.

### 3.5 What a rollback leaves behind (disclosure, owner condition)

**A rollback of `AddPatientMrn` does not return the physical schema to its byte-for-byte pre-migration state.** It retains:
- the `PatientMrnArchive` table and its rows;
- the table's primary key, unique constraint and foreign key to `Patients`;
- guard trigger `TR_PatientMrnArchive_Preserve` (51302).

The `Patients` table itself returns to its pre-migration shape. The retained table is invisible to the application below this migration (nothing older references it). The foreign key does block deleting a patient who has an archived number. Patients are never deleted, so this changes no behaviour; it is disclosed because the schema then differs from the pre-migration state.

## 4. Guards and codes after this change

| Code | Where | Meaning |
|---|---|---|
| 51298 | `TR_Patients_MrnImmutable` | an MRN never changes (unchanged) |
| 51300 | `AddPatientAccessAudit` Down (slice 5) | Down refused while access/session audit rows exist (no longer used by `AddPatientMrn`) |
| **51302** (new) | `TR_PatientMrnArchive_Preserve`; `AddPatientMrn` Down step 1 | an archived MRN never changes or disappears; Down refuses when the archive and the patients disagree |

51299 and 51301 stay as planned for slice 5.

## 5. Tests (focused; replaces the `MrnMigrationTests` line of brief section 8)

- **`MrnMigrationTests`:**
  - **Up on a populated database:** every patient numbered, numbers unique, deterministic (a second database with the same rows gives the same numbers), merged-away rows numbered.
  - **Down → Up restoration:** patients created after Up (the case 51300 used to refuse) keep exactly their numbers after Down and Up again. A patient created while rolled back gets a new number that is not any archived number.
  - **Archive conflict refusal:**
    - an archive row altered by direct SQL is refused by 51302;
    - with the guard bypassed in the test setup (trigger disabled), an archive/patient mismatch makes Down refuse with 51302 and changes nothing;
    - a delete from the archive is refused by 51302.
  - **Archive on insert:** a registered patient has its archive row; a registration that fails (or a retried MRN collision) leaves no orphan archive row.
  - **Uniqueness:** a duplicate MRN in the archive is refused by its unique constraint, and the run-time allocator never hands out a number that is only in the archive.
  - **The retained table is disclosed:** after Down, `PatientMrnArchive` exists and `Patients` has no `Mrn` column, index or guard.
- **`MrnTests`** (as approved; the allocator's "taken" now includes the archive): generation, collision step, a concurrent registration race over 20 rounds, the retry cap, guard 51298, search precedence, merge and unmerge keep MRNs, and a new number archived in the same save.
- **The existing migration-test rollback pattern:** the focused group includes representative existing classes that create patients and then roll back past `AddPatientMrn`: `RetiredRecordGuardTests`, `PatientDuplicateReviewTests`, `PatientMergeSchemaTests`, `PatientUnmergeSchemaTests`. They must pass unchanged. The other rollback classes are covered by the ALV-N028 full regression.
- **Controls (brief section 8 list, added to):**
  - with Down step 1 removed, the conflict test must fail;
  - with the restore step removed, the down → up test must fail.

## 6. Unchanged

The MRN value rule (UTC whole seconds since 0001-01-01, 11 digits), the run-time collision retry, immutability (51298), never reused, merge and unmerge keeping numbers, display and search, the export-file `source.mrn` member (owner decision 2026-11-19: part of the pre-release version-1 contract) and the out-of-scope printed documents (OQ-2 → ALV-N026).

## 7. Assumptions (logged)

1. Patients are never deleted (brief assumption 4); the foreign key relies on it and enforces it for archived patients.
2. The extra run-time query (archive look-ahead) costs the same as the existing one: an index seek on a bounded range.
3. Archiving on insert (3.4) is the recommendation; the reviewer may choose Down-time archiving instead without changing anything else in this addendum.
4. A trigger on `Patients` that inserts into another table runs inside the inserting statement's transaction (SQL Server semantics), so the patient and its archive row commit or roll back together, including the allocator's retried saves.
