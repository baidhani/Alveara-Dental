# Clinical documentation: encounters, history, allergies and medications (STORY-005)

STORY-005 is the first clinical module. A clinician documents an **encounter** for a patient - medical history, dental history, allergies and medications - finalizes it, and
can only amend it afterwards by adding an **addendum** that sits beside the untouched original. Every change is logged with the signed-in user and a timestamp.
It satisfies REQ-007 ("structured clinical documentation including medical and dental history, allergies, and medications"). It is a deliberately small first
version: later stories add vitals, SOAP/progress notes, templates and longitudinal history (`ALV-005-C01`), patient-safety alerts (`ALV-N011`) and the odontogram
(`STORY-006`) on top of it without changing what is described here.

## The model (`Architecture/Clinical/`)

| Record | What it is |
|---|---|
| `Encounter` | One encounter for a patient: when it took place, an optional link to the appointment it documents (at most one encounter per appointment), a status of `Draft` or `Finalized`, who finalized it and when, a row version, and an optional start key that makes a retried start return the first encounter. |
| `EncounterEntry` | One structured item: a medical-history item, a dental-history item, an allergy (reaction, severity) or a medication (dose, frequency). A **removed** entry is kept (who and when) and only hidden; nothing clinical is ever deleted. |
| `EncounterSectionMark` | "Reviewed - none reported" for one of the four sections, so documentation can be completed without inventing an entry. |
| `EncounterAddendum` | Text added to a **finalized** encounter, with who and when and a client key (a retried submit returns the first addendum). Append-only. |
| `EncounterEvent` | The encounter's own append-only history, one row per change. Its text is PHI-free (section names only). |

The four sections are `MedicalHistory`, `DentalHistory`, `Allergy` and `Medication`. A section is **Recorded** (at least one active entry), **NoneReported** (reviewed, nothing
to report) or **Empty** (not yet addressed). An encounter is *complete* when no section is Empty.

## The rules

- **Finalizing** needs every section Recorded or NoneReported; otherwise it is refused with a 409 `documentation_incomplete` that names each section still Empty. A section
  marked NoneReported cannot also hold entries, and a section with entries cannot be marked.
- **Finalized means finalized.** The service refuses any change to a finalized encounter (`encounter_finalized`), and database triggers refuse it independently, so a bug
  or a hand-written update cannot rewrite the chart. The only addition is an **addendum**.
- **An addendum** is accepted only on a finalized encounter (`encounter_not_finalized` otherwise), needs text and an `Idempotency-Key`, and is appended with who and when.
  The original entries, section reviews and the encounter's version are not touched at all.
- **Repeats are quiet.** Starting with the same key or for the same appointment, adding an identical entry (even retyped in another case), removing a removed entry, marking a
  marked section, finalizing a finalized encounter and re-sending an addendum with the same key all return the current state and change nothing. The same key with
  *different* content is refused (`idempotency_key_reused`), never silently merged.
- **Validation.** A name is required; fields that do not belong to a section are refused (a reaction on a medication, a dose on an allergy); severity is Mild, Moderate or Severe,
  or blank when unknown. Limits: name 200, details 1000, reaction 200, dose 100, frequency 100, addendum 4000 characters.
- **Concurrency.** Every change to a draft echoes the encounter's `rowVersion`, and every accepted change moves it, so two clinicians editing the same note cannot overwrite each
  other: the second gets the shared 409 `concurrency_conflict`. A change made from a version that was finalized in the meantime cannot slip in.

## Permissions

| Permission | Who | Used for |
|---|---|---|
| `ViewClinicalDocumentation` (new) | Dentist, Hygienist, Assistant, Admin | reading encounters, entries, addenda and history |
| `ManageClinicalNotes` (existing) | Dentist, Hygienist, Admin | starting, documenting, finalizing and adding addenda |

Reading is a separate permission on purpose: front desk and billing hold `ViewPatientRecords` to find a patient, and that must not open a medical history to them.
Front desk, billing and the practice manager have no clinical read; an assistant reads but cannot write.

## API (`Controllers/ClinicalController.cs`)

| Endpoint | Purpose |
|---|---|
| `GET api/patients/{id}/encounters`, `GET api/encounters/{id}` | list and read |
| `POST api/patients/{id}/encounters` | start a draft (`Idempotency-Key`; 201, or 200 when it already exists) |
| `POST api/encounters/{id}/entries`, `PUT .../entries/{entryId}`, `POST .../entries/{entryId}/remove` | add, change, remove an entry |
| `POST api/encounters/{id}/sections/{kind}/none-reported`, `.../clear-review` | mark / clear "reviewed - none reported" |
| `POST api/encounters/{id}/finalize` | finalize |
| `POST api/encounters/{id}/addenda` | add an addendum (`Idempotency-Key`) |

Every change needs a CSRF token and the row version the caller read. A refusal returns a stable `error` code, a message safe to show and, where several things need
attention, `fieldErrors`.

## Audit and history

Each change, its history row and its audit entry are staged on one context and saved **once**, so they stand or fall together: if the audit entry cannot be written the
change is not made, and the same request can simply be retried. Audit events: `EncounterStarted`, `EncounterEntryAdded`, `EncounterEntryChanged`, `EncounterEntryRemoved`,
`EncounterSectionReviewed`, `EncounterSectionReviewCleared`, `EncounterFinalized`, `EncounterAddendumAdded`. Their text carries section names and ids only; names, reactions,
doses and notes live in the clinical tables behind the clinical permissions.

## What the database enforces (migration `AddClinicalEncounters`)

Case-sensitive checks on status, section, state and severity (the default collation would accept `finalized`); a finalized encounter has finalizer stamps and a draft has
none; triggers that refuse any change to a finalized encounter and to its entries and reviews, refuse an addendum on anything but a finalized encounter, make addenda and
history append-only, and forbid deleting encounters and entries. One encounter per appointment and one review per section are unique.

## The screen (`pages/clinical/`)

A **Clinical** tab in the patient workspace, shown only to roles that may read it. The list marks each encounter in words as "Draft - not finalized" or "Finalized" and whether all
four sections are addressed. The encounter view has the four sections (add, change and remove entries; "Reviewed - none reported"), a status line that says in words whether the
last change was saved, is saving or failed, an inline "Review and finalize" that lists unresolved sections and disables Finalize until none remain, and, once finalized, a read-only
note with its addenda beside it and a box to add one. Typed values survive a refused or failed save, a dropped connection and a conflict reload (the screen refreshes in place),
and start and addendum keep one idempotency key across retries.

## Tests

`ClinicalEncounterSchemaTests` (database rules, real SQL Server), `EncounterServiceTests` (rules, audit, every failure path, races), `EncountersApiTests` and
`ClinicalPermissionTests` (roles, CSRF, versions, keys, error shapes), `Clinical.test.tsx` and `clinicalContrast.test.ts` (screen, jsdom), and the real-browser walkthrough
`e2e/clinical-real-backend.spec.ts` (see `docs/testing/REAL_BACKEND_E2E.md`).

## Known limits (first version)

- Documentation is **per encounter**: there is no running medical history, allergy list or medication list across encounters, no reviewed-by attribution over time, and no
  active/inactive/resolved lifecycle for allergies or medications (`ALV-005-C01`).
- No vitals, SOAP/progress notes, treatment notes, templates, free-form clinical note body or signature step; "finalizing" is the sign-off and records the finalizer.
- The only "unknown" state is "reviewed - none reported"; "not reviewed" and "unknown" as distinct states come with `ALV-005-C01`.
- An addendum is plain text; it cannot be edited, retracted or attached to a specific entry.
- A finalized encounter cannot be reopened, and there is no "close open drafts" flow.
- Clinical alerts (a severe allergy banner on the schedule or chart) are not built (`ALV-N011`); the odontogram and treatment plans are separate stories.
- Reading an encounter is not itself audited (only changes are).
