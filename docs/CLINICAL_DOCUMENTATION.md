# Clinical documentation: encounters, history, allergies and medications (STORY-005)

STORY-005 is the first clinical module. A clinician documents an **encounter** for a patient - medical history, dental history, allergies and medications - finalizes it, and
can only amend it afterwards by adding an **addendum** that sits beside the untouched original. Every change is logged with the signed-in user and a timestamp.
It satisfies REQ-007 ("structured clinical documentation including medical and dental history, allergies, and medications"). It is a deliberately small first
version: `ALV-005-C01` (the companion section at the end of this document) adds vitals, SOAP/progress/treatment notes, templates, signing and the longitudinal record on top
of it without changing what is described here; patient-safety alerts (`ALV-N011`) and the odontogram (`STORY-006`) come later.

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

## Known limits of the STORY-005 encounter documentation

These describe the encounter documentation as STORY-005 left it; the companion section below says what `ALV-005-C01` added and what is still not built.

- A STORY-005 encounter's four sections are per encounter. The running chart across encounters, reviewed-by attribution over time and the active/inactive/resolved
  lifecycle came with `ALV-005-C01`.
- "Reviewed - none reported" is the encounter's own "unknown" state; `none known`, `unknown` and `not reviewed` as distinct statements about the patient's record came with
  `ALV-005-C01`.
- A finalized encounter cannot be reopened, and there is no "close open drafts" flow.
- Clinical alerts (a severe allergy banner on the schedule or chart) are not built (`ALV-N011`); the odontogram and treatment plans are separate stories.
- Reading an encounter is not itself audited (only changes are).

---

# Companion: `ALV-005-C01` - longitudinal record, notes, templates, vitals, signing and amendments

`ALV-005-C01` completes the documentation workflow. STORY-005's contract is unchanged and its tests still pass untouched: everything here is additive (new tables, new
optional columns, new endpoints, new fields on the encounter view, new screens). Finalizing an encounter without a template behaves exactly as before.

## The longitudinal record (`ClinicalRecord.cs`)

STORY-005 documents what was recorded at one visit. The **clinical record** is the running chart across visits: medical history, dental history, allergies and
medications that stay true until a clinician says otherwise.

| Record | What it is |
|---|---|
| `ClinicalRecordItem` | One item of a section (name, detail; allergies add reaction and severity, medications dose and frequency) with a **status**: `Active`, `Inactive`, `Resolved` (history and allergies) or `Discontinued` (medications). A database check keeps the status valid for the kind, case-sensitively. One live item per name per section per patient (a unique index). Never deleted: an item entered in error is marked removed, with a required reason. |
| `ClinicalRecordItemVersion` | Append-only history: a full snapshot after every add, change, status change and removal - who, when, why (when the clinician said) and which encounter it was made during. A trigger refuses any edit. This is the history-preserving correction: what an item said before is always there. |
| `ClinicalSectionReview` | What a clinician *says* about a section: `Reviewed` (the listed items are current), `NoneKnown`, or `Unknown`, with who and when. No row at all means **not reviewed**. |
| `ClinicalRecordEvent` | Append-only, PHI-free timeline of the record's changes. |

### Unknown / not reviewed / none known

These are three different statements and none of them is an item, so a required field never forces an invented fact:

- **Not reviewed** - nobody has said anything about the section (no row). It is what a new patient's record shows.
- **None known** - a clinician reviewed the section and nothing is known to report.
- **Unknown** - a clinician could not establish it (for example the patient could not say).

The section's status shown to readers is *derived* from its items and its review row: `Reviewed` (items listed and confirmed after the last change), `NeedsReview` (items
listed but never confirmed, or an item changed after the confirmation), `NoneKnown`, `Unknown`, `NotReviewed`. "None known" and "unknown" need an empty section; "confirm
the list is current" needs at least one item; adding an item to a section that says none known / unknown withdraws that statement in the same save (and records that it did).
Saying the same thing again records nothing.

## Notes, vitals and templates (`EncounterNotes.cs`)

- **Notes** - `EncounterNote`: free text for one of six sections (Subjective, Objective, Assessment, Plan - SOAP - plus Progress and Treatment notes), one row per encounter
  and section, up to 8000 characters. They save themselves as they are written. A note changes only on an unsigned draft; triggers refuse any change once the encounter is
  finalized.
- **Vitals** - `EncounterVitals`: blood pressure (as a pair, diastolic below systolic), pulse, respirations, temperature (C), oxygen saturation, weight (kg) and height (cm),
  attributed to the encounter, the patient, the time measured and who recorded them. Every value has a plausible range and a stated precision (a typo guard, not a
  diagnosis; one decimal place, never silently rounded). A reading is **never edited**: a mistake is *voided* (kept, with who and why) and recorded again; a trigger refuses any
  other change. A client key makes a retried save return the first reading.
- **Templates** - `NoteTemplate` / `NoteTemplateSection`: which note sections a note has, which are **required before signing**, and optional starter text. They belong to the
  clinical-documentation domain (their own tables, endpoints under `api/clinical/templates`, and their own permission `ManageClinicalTemplates`), not to generic practice
  configuration. A template is deactivated, never deleted. Applying one copies its sections, required flags and starter text onto the encounter (once per encounter), so
  editing a template later never changes a note already using it. A required note that is blank *or still exactly the starter text* counts as not written.

## Signing and amending

An encounter now moves **Draft -> (Signed) -> Finalized**.

- **Signing** is the clinician's attestation that the note is complete. It needs the same things finalizing needs (the four sections addressed and every required note
  written) and refuses with a 409 `documentation_incomplete` naming each unresolved section and note (`note:Plan`). A signed draft is **locked**: every change is refused with
  `encounter_signed` until it is unsigned (audited; who signed and who unsigned are both kept) or finalized. To the database a signed encounter is still a Draft - the lock is
  the service's.
- **Finalizing** is unchanged except that it also needs the required notes; it needs no further signature after signing, and records its own finalizer. Both who signed and
  who finalized are shown.
- **Amending** is still an addendum, now with optional **what it amends** (a documentation section, a note section, or the vital signs). The original is never touched; the
  encounter screen shows a timeline - when the original was finalized and by whom, then each addendum with its author, time and section.

## Permissions

| Permission | Who | Used for |
|---|---|---|
| `ViewClinicalDocumentation` (STORY-005) | Dentist, Hygienist, Assistant, Admin | reading the record, its history, notes, vitals and templates |
| `ManageClinicalNotes` (STORY-005) | Dentist, Hygienist, Admin | changing the record, writing notes, recording and voiding vitals, applying a template, signing, unsigning, finalizing, amending |
| `ManageClinicalTemplates` (new, appended last) | Dentist, Admin | creating, changing and deactivating note templates (and seeing the ones taken out of use) |

## API

| Endpoint | Purpose |
|---|---|
| `GET api/patients/{id}/clinical-record`, `GET api/clinical-record/items/{itemId}/history` | the record (sections, items, statuses, who reviewed, timeline) and one item's full history |
| `POST api/patients/{id}/clinical-record/items`, `PUT api/clinical-record/items/{itemId}`, `POST .../status`, `POST .../remove` | add, correct, change the status of, or remove (entered in error, reason required) an item; changes echo the item's `rowVersion` |
| `PUT api/patients/{id}/clinical-record/sections/{section}/review` | state `Reviewed`, `NoneKnown`, `Unknown` or `NotReviewed` (withdraw) |
| `PUT api/encounters/{id}/notes/{section}`, `POST api/encounters/{id}/template` | save a note; apply a template |
| `POST api/encounters/{id}/vitals` (`Idempotency-Key`), `POST .../vitals/{vitalsId}/void` | record a reading; void one |
| `POST api/encounters/{id}/sign`, `POST api/encounters/{id}/unsign` | sign; withdraw the signature |
| `GET/POST api/clinical/templates`, `GET/PUT api/clinical/templates/{id}`, `POST .../{id}/active` | the templates |
| `POST api/encounters/{id}/addenda` | now also takes an optional `section` |

Every change needs a CSRF token and the version it read; a stale edit is the shared 409 `concurrency_conflict`. A refusal returns a stable `error` code, a message safe to show
and, where several things need attention, `fieldErrors`.

## Audit and history

Every change is staged with its history row and its audit entry and saved once, so they stand or fall together. Audit events: `ClinicalRecordItemAdded`,
`ClinicalRecordItemChanged`, `ClinicalRecordItemStatusChanged`, `ClinicalRecordItemRemoved`, `ClinicalSectionReviewed`, `ClinicalSectionReviewCleared`, `ClinicalTemplateCreated`,
`ClinicalTemplateUpdated`, `ClinicalTemplateActivated`, `ClinicalTemplateDeactivated`, `EncounterNoteSaved`, `EncounterTemplateApplied`, `EncounterVitalsRecorded`,
`EncounterVitalsVoided`, `EncounterSigned`, `EncounterUnsigned`, and (with the section) `EncounterAddendumAdded`. Their text carries section names and ids only - never an item, a
note, a measurement or a reason.

## What the database enforces (migration `AddClinicalRecordAndNotes`)

Case-sensitive checks on kind, status (per kind), review state, change type, note and template sections; one live item per name per section, one review per section, one note
per section, one key per encounter's vitals; the vitals "at least one value, blood pressure as a pair, diastolic below systolic, void stamp all-or-nothing" checks; and triggers
(51040-51048) that refuse deleting items, notes, vitals and templates, any edit of item versions or the record's events, any edit of a recorded vital sign other than voiding it,
and any note or vitals write on a finalized encounter.

## The screens

The Clinical tab opens on the **clinical record** (history summary across encounters): each section with its items, each item's status in words, who added and last changed
it, the section's statement in words (reviewed by whom and when, needs review, none known, unknown, not reviewed), per-item history, and - for a clinician - add, correct,
change status, remove as entered in error, confirm the list, and state none known or unknown. Below it are the encounters and a link to the **note templates** page.
The encounter screen adds the sticky save indicator, vital signs, notes (they autosave on a pause and on leaving the box; a failed save keeps the text and offers "Save note"),
template choice, "Sign note" in the review (listing every unresolved section and required note), the signed banner with "Unsign to keep editing", and the amendment timeline.
Typed text survives a refused save, a dropped connection and a stale edit (reloads refresh in place).

## Tests (ALV-005-C01)

`ClinicalRecordSchemaTests` (database rules), `ClinicalRecordServiceTests` and `EncounterNotesServiceTests` (rules, lifecycle, attribution, semantics, audit, every failure path,
races), `ClinicalRecordApiTests` and `ClinicalPermissionTests` (roles, CSRF, versions, keys, error shapes); `ClinicalRecord.test.tsx`, `ClinicalNotes.test.tsx` and
`clinicalNotesContrast.test.ts` (screens, jsdom); and the real-browser walkthrough `e2e/clinical-companion-real-backend.spec.ts` (see `docs/testing/REAL_BACKEND_E2E.md`).

## Known limits (still not built)

- AI-assisted draft generation (`ALV-N007`); the odontogram and treatment plans (`STORY-006` onward). Patient-safety alerts and clearance tracking are built in `ALV-N011` - see `docs/PATIENT_SAFETY.md`; they read this record's active allergies and medications and never copy them.
- A note's *draft* text is not versioned (autosave overwrites the draft); the finalized note is immutable and amended by addendum, and the events list when a note was saved.
- The longitudinal record is not copied into an encounter automatically: an encounter's four sections (STORY-005) and the patient's record are separate, and a record change is
  linked to an encounter only through the API (`encounterId`), not yet from the encounter screen.
- Signing is the clinician's attestation recorded with a name and time; there is no separate credential or cosigning step, and any clinician with `ManageClinicalNotes` may
  unsign a signed draft (it is audited).
- Vitals are metric only, and entered as numbers; there is no device import, trending chart or percentile calculation.
- Reading the record or an encounter is not itself audited (only changes are).
- A template can be applied once per encounter; there is no "change template" after one is applied.
