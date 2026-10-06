# Structured diagnoses (STORY-013, ALV-013-C01)

**Requirement:** REQ-010 - the system must allow structured diagnosis linked to patient, encounter, and treatment plan.
**Where it lives:** the patient workspace's **Diagnoses** tab (`/patients/:id/diagnoses`), in the clinical documentation module beside the encounter notes, the odontogram and periodontal charting.
**Who can use it:** anyone who may read clinical documentation (`ViewClinicalDocumentation`: dentist, hygienist, assistant, admin) can see the diagnoses and their history; recording, correcting and withdrawing need `ManageClinicalNotes` (dentist, hygienist, admin) and a CSRF token. Front desk, billing and the office manager cannot see the tab and are refused (403) by the API. No new permission was added.

## What it does

A diagnosis is a structured clinical statement made in one encounter of one patient.

- **Linked to the patient and the encounter.** Both are required and authoritative. The encounter must be one of **this patient's** encounters (an encounter of another patient and one that does not exist are refused identically, `encounter_not_found`, so the answer does not reveal whether the encounter exists for someone else). The database refuses anything else, and neither link, nor the diagnosis's origin, can ever change. A diagnosis can be recorded against a draft or a finalized encounter: it changes nothing in the encounter's notes.
- **The fields.** A required **label** (1 to 200 characters, one line), an optional **tooth** (one of the odontogram's 52 FDI keys, shown in the numbering in use and stored as FDI), optional **notes** (up to 1,000 characters, may span lines) and an optional **treatment-plan reference**.
- **The treatment-plan reference is a forward reference.** Treatment plans do not exist yet (they belong to a later story), so it is an optional, opaque piece of text kept beside the diagnosis in the style of the odontogram's and the periodontal chart's links: trimmed, runs of spaces collapsed, up to 100 characters, one line. It is **not a foreign key, never looked up, and says nothing about whether a plan exists**; the API returns `treatmentPlanReferenceState: "Unresolved"` whenever one is present and the screen always labels it "Treatment plan reference (unresolved)" with the plain statement that it does not mean a plan with that reference exists. Absent means none; present but blank is refused rather than silently dropped. It is carried in every history version, kept through every correction and withdrawal, and **removed or replaced only by an explicit, recorded act** (see below).
- **Incorrect data is refused, with every problem named.** The entry is judged whole before anything is written: every problem is listed together with its field, a stable code and a message that says what to correct, and one problem means nothing is saved (`validation_failed`, 400). The screen applies the same rules first, lists the problems with a link to each box and moves focus to the list; a server refusal is shown the same way and everything typed is kept.
- **Corrections and withdrawal.** A diagnosis is **never deleted**. A wrong one is **corrected** (label, tooth, notes; a reason is required; a new history version is appended) or **withdrawn** (a reason is required; it stays in the record, marked Withdrawn, with who, when and why). A correction that changes nothing is quiet; withdrawing a withdrawn diagnosis is quiet; a withdrawn diagnosis cannot be corrected (`diagnosis_withdrawn`, 409). A stale change is the shared 409 concurrency conflict (the diagnosis's row version).
- **How the reference changes.** A correction that leaves the reference out **keeps it**. Giving a value **replaces** it. Only `clearTreatmentPlanReference: true` **removes** it. Replacing and clearing together is refused, and a blank replacement is refused rather than read as a clear. On screen these are three explicit choices ("Keep it", "Replace it" or "Add one", "Remove it"), and the last two say they are recorded in the history with the reason. Every value the reference ever had is in the history.
- **History.** Each diagnosis opens to its history: every step, oldest first, with who, when, what happened, the whole diagnosis as it stood, the reference at that step and the reason.

## How the promises are kept

| Promise | How |
|---|---|
| A diagnosis is linked to the patient and the treatment plan (reference) | `PatientId` and `EncounterId` are required foreign keys with a trigger that refuses an encounter of another patient (51074) and any later change of patient, encounter or origin (51073); the plan link is the typed forward reference, stored, shown and audited, never resolved. |
| Incorrect data is rejected and the person prompted to correct it | `DiagnosisRules.Check` returns every problem or the normalized entry, never a partial one; the database repeats the rules as check constraints (blank label, control characters, tooth not an FDI key, blank reference, bad status, a withdrawal without its stamp). The client mirror of the rules is held to the server's by the same test cases. |
| Every entry is logged with user and timestamp | The diagnosis, its history version and a PHI-free audit entry (never the label, tooth, notes or reference) are saved **once**: if the audit write fails nothing is saved and the person is told (`save_failed`, 503, without internals). Recording, correcting and withdrawing each write one. |
| Repeats and retries are safe | Recording carries an idempotency key, unique per patient: the same key and entry returns the diagnosis already saved (also after it has been corrected), the same key with a different entry is refused (`idempotency_key_reused`, 409), and eight simultaneous saves make one. The screen keeps the key while the entry is unchanged and renews it when anything changes. |
| Nothing is lost | A diagnosis is never deleted (trigger 51071); its history is append-only (51072); the reference survives corrections and withdrawal. |
| Nothing is invented | An empty list says only that none has been recorded; a failed load says it could not load; the reference is never presented as proof that a plan exists. |

## API

- `GET /api/patients/{id}/diagnoses?encounterId=&includeWithdrawn=` - the patient's diagnoses, newest first (withdrawn only on request). `ViewClinicalDocumentation`.
- `POST /api/patients/{id}/diagnoses` - body `{ idempotencyKey, encounterId, label, toothKey, notes, treatmentPlanReference }`; returns the diagnosis. `ManageClinicalNotes` plus a CSRF token.
- `GET /api/diagnoses/{id}` and `GET /api/diagnoses/{id}/history`.
- `POST /api/diagnoses/{id}/correct` - body `{ rowVersion, label, toothKey, notes, treatmentPlanReference, clearTreatmentPlanReference, reason }`; `POST /api/diagnoses/{id}/withdraw` - body `{ rowVersion, reason }`.
- Refusals carry `error`, `message` and `problems`: `validation_failed` (400), `reason_required` (400), `row_version_required` and `row_version_invalid` (400), `patient_not_found`, `encounter_not_found` and `diagnosis_not_found` (404), `idempotency_key_reused` and `diagnosis_withdrawn` (409), `save_failed` (503); a stale change is the shared `concurrency_conflict` (409).

## Schema (migration `AddDiagnoses`)

`Diagnoses` (patient, encounter, idempotency key, label, tooth, notes, treatment-plan reference, status Active or Withdrawn, who and when for the record, the last change and the withdrawal, a row version) and `DiagnosisVersions` (a full snapshot after every record, correction and withdrawal). Triggers 51071 (a diagnosis is never deleted), 51072 (the history is append-only), 51073 (the patient, encounter and origin never change) and 51074 (an encounter must be the patient's); the next free number is 51075. There is **no treatment-plan table** and the reference column has no foreign key.

## Decisions

1. **The plan link is a forward reference, not a plan record.** Treatment planning is a later story; building a placeholder plan would pre-empt its design. A reference keeps the link the requirement asks for without claiming a plan exists, and a later story can reconcile it.
2. **The patient and the encounter are the authoritative links**, enforced by the database, not by the service alone.
3. **The reference changes only by an explicit, recorded act**, so it can never be dropped or swapped by a correction that merely left it out.
4. **A diagnosis can be recorded against a finalized encounter**, since it is its own record with its own history and does not alter the encounter's notes.
5. **Corrections and withdrawals need a reason and keep everything**; a wrong diagnosis is withdrawn, never deleted.
6. **Reading and writing use the existing clinical-documentation permissions**, so no role matrix changed (hygienists can record, as they can write clinical notes).
7. **An encounter of another patient and a missing encounter look the same** to the caller.

## Structure, lifecycle, links and amendment (ALV-013-C01)

The companion makes a diagnosis a structured, attributable clinical statement without changing any of STORY-013's behaviour (its tests are unchanged and pass).

- **Optional coding.** A diagnosis needs no coding: with none it is stored structurally and reads as `Manual` and uncoded. A coding is a system and a code **together**. The system is one of `ICD-10-CM`, `SNODENT` or `Local` (only the system's **name**: no terminology content is bundled and a code is **never checked against a code set**). A code is 1 to 30 letters, digits, dots, hyphens or underscores. An unknown system is refused by name.
- **Provenance.** `Source` is `Manual` (the default), `Imported` or `Mapped`, with an optional source note (up to 200 characters) that is allowed only on an imported or mapped diagnosis, so data brought in later still says where it came from.
- **Place.** A diagnosis is about one tooth **or** one oral region (`FullMouth`, `UpperArch`, `LowerArch`, the four quadrants, `SoftTissue`, `Tmj`), never both.
- **Amendment.** `POST /api/diagnoses/{id}/amend` replaces the whole structure (tooth or region, coding, source) as one recorded `Amended` act with a required reason and the row version. The previous values stay in the history with who, when and why. The label, notes and the treatment-plan reference (with its provenance) are carried through untouched. An amendment that changes nothing is quiet.
- **Status.** `Active`, `Resolved` or `Withdrawn`. Resolve and reactivate (`/resolve`, `/reactivate`) each need a reason, are history entries and are reversible; a repeat is quiet. `Withdrawn` stays final: a withdrawn diagnosis cannot be amended, resolved, reactivated or linked (`diagnosis_withdrawn`, 409). The default list shows Active and Resolved.
- **Links.** `POST /api/diagnoses/{id}/links` links a diagnosis to an odontogram **finding** or a periodontal **chart** (`Finding`, `PerioExam`) of the **same patient**. Links are append-only (trigger 51075), unique per diagnosis, type and target, carry who and when, and a target that is missing or belongs to someone else is refused identically (`link_target_not_found`, 404; trigger 51076 repeats it in the database). Linking is idempotent.
- **The treatment-plan reference stays a forward reference.** Nothing here looks it up or builds a plan. A request that tries to mark it as anything but `Unresolved` is refused (`treatmentPlanReferenceState:not_supported`, 400) on record, correct and amend, and it is carried unresolved, with its own actor and time, through every amendment, status change and history step.
- **Nothing is destroyed.** No diagnosis, history version or link can be deleted (51071, 51072, 51075); a diagnosis that carries a treatment-plan reference or links is amended, never overwritten.

Schema: migration `AddDiagnosisStructure` (columns on `Diagnoses` and `DiagnosisVersions`, the `DiagnosisLinks` table, check constraints for system/code pairing, characters, source, note and region, and triggers 51075 and 51076). The next free trigger number is **51077**. On screen: coding, source and region on the record form, context chips and linked records on each diagnosis, Amend, Mark resolved / Make active again and Link actions (each but Link asks why), and amendment, resolve and reactivate steps in the history.

### Decisions

1. **Amendment is a separate act from correction.** Correction (label, tooth, notes) is unchanged from STORY-013 and keeps its history type; amendment is for structure, so the history says which kind of change was made.
2. **Coding systems are names only and the list is closed.** No licensed content is bundled; adding a system is a model and rule change, not data.
3. **Resolved is not Withdrawn.** Resolved means no longer present and is reversible; withdrawn means entered in error and is final.
4. **Links go to records that really exist, with the same-patient rule in the service and the database.** A treatment plan is not a link type: its reference lives on the diagnosis.
5. **A refused plan-state change instead of a new endpoint.** The "resolve the reference" failure path is covered by refusing the attempt, so no treatment-plan API is built.

## Limits (recorded, not hidden)

- **The code is never validated against a code set**, and the list of coding systems is closed to three names; importing or mapping a terminology is future work and must not change a diagnosis's identity or history.
- **The treatment-plan reference is never resolved or checked.** Nothing verifies that a plan with that reference exists, and it is not matched to a plan when plans are built; reconciling references with real plans, with same-patient validation, idempotency and visible handling of unresolved or invalid references, is the later treatment-plan story's work.
- **Status is Active, Resolved or Withdrawn only**; there are no chronic or staged states, and no automatic diagnosis from findings or charts (a link records that a clinician tied them together, it does not claim one proves the other).
- **A diagnosis cannot be moved to another encounter** (the links never change); withdraw it and record a new one.
- **A diagnosis needs an encounter**; a patient with none must have one started in the Clinical tab first.
- **Links are one-way and permanent**: there is no unlink (a wrong link is recorded as a wrong link; withdraw the diagnosis if it was wrong), and a diagnosis cannot yet link to an encounter note, procedure or image.
- **One tooth or one region per diagnosis**, no surface.
- **Correcting the tooth of a diagnosis that has a region is refused** (a diagnosis is about a tooth or a region): amend it instead.
- **Hygienists can record and amend diagnoses** because they hold the clinical-notes permission; narrowing that is a permission-model decision.

## Verification

Backend, against real SQL Server: `DiagnosisRulesTests` (the rules and boundaries), `DiagnosisSchemaTests` and `DiagnosisMigrationTests` (the database's own refusals, triggers and the migration up and down), `DiagnosisServiceTests` (behaviour, audit and save failure, idempotency and simultaneous saves, corrections and the reference), `DiagnosisApiTests` (roles, CSRF, refusal shape, workflow). Frontend: `diagnosisRules.test.ts` (held to the server's cases), `DiagnosisApi.test.ts` (client against the fake) and `Diagnoses.test.tsx` (the screen, every failure path, axe). Real browser, un-mocked: `e2e/diagnoses-real-backend.spec.ts` and, for the companion, `e2e/diagnoses-structure-real-backend.spec.ts` (see `docs/testing/REAL_BACKEND_E2E.md`). ALV-013-C01's tests: `DiagnosisStructureRulesTests`, `DiagnosisStructureSchemaTests` (with the migration up, down and up again), `DiagnosisStructureServiceTests`, `DiagnosisStructureApiTests`, `diagnosisStructureRules.test.ts`, `DiagnosisStructureApi.test.ts` and `DiagnosisStructure.test.tsx`.
