# Versioned forms, consents and e-signature foundation (ALV-N010)

How forms and consents work, for the stories that build on them (notably `ALV-010-C01`, the document/image library, and the check-in and treatment-plan stories
that need signed consents). The wording of every form is the practice's own: this system records **what was shown and who signed it**; it does not make any
wording legally sufficient, and a typed signature on its own does not establish legal sufficiency for every consent scenario. Legal review of actual form text is
outside this software.

## The model
- **`FormTemplate`** - a stable identity (`Key`, category, active flag, which version is current). Categories: Privacy, Financial, General consent, Treatment.
- **`FormTemplateVersion`** - one immutable published version: title, wording, field definitions (JSON), a SHA-256 content hash. **Editing a template appends a new
  version; no version is ever updated or deleted.** Re-saving unchanged content publishes nothing.
- **`PatientForm`** - one patient's instance: `Draft` -> `Signed` -> (`Void`), or `Draft` -> `Void`. A draft is **pinned to the template version it started on**;
  a later template edit never changes it - staff are offered the newer version and may move the draft (the old draft is voided as "replaced", every answer that
  still fits is carried over). One open draft per patient per template (a unique filtered index; a second "start" returns the open draft).
- **`SignedFormSnapshot`** - the signed artifact: a verbatim copy of the template version's wording and fields, the responses **as saved at signing**, the signer's
  name, relationship to the patient (Self, Parent, Legal guardian, Spouse, Legal representative, Other + description), signature method (`typed-name`) and text,
  the attestation text agreed to, the time, the staff member who captured it, and a SHA-256 over all of it. **Immutable**: at most one per form (unique index);
  application code cannot update or delete it (`SignedRecordImmutableException`) and **database triggers refuse any UPDATE/DELETE on `SignedFormSnapshots` and
  `FormTemplateVersions`** from any path.
- **`PatientFormEvent`** - append-only history: Started, Signed (with signer name/relationship), Voided (with reason), Superseded.

The signer is **free text plus a relationship**, not necessarily a registered patient (a guarantor or parent need not be one) - this answers the question the
ALV-003-C01 handoff left for this story.

## Signing
`POST /api/forms/{id}/sign` carries the draft's row version, the template version id the user reviewed, the signer, relationship, typed signature, the
attestation tick, and an `Idempotency-Key`. In **one atomic save** the snapshot, status change, history event, audit entry and idempotency receipt commit together
or not at all:
- **Interrupted**: if the save fails (storage failure, cancelled request), nothing is half-written - the form is still a plain draft and the same key can be retried.
- **Duplicate submit**: the same key again returns the first result (200, nothing new); a *different* key on an already-signed form is `409 already_signed`
  (with the existing snapshot id); eight simultaneous submits leave exactly one snapshot (idempotency receipt + the one-snapshot-per-form index).
- **Changed after review**: a draft edited after the user reviewed it is a `409 concurrency_conflict`, never a signature on different answers.
- **Missing signer identity / relationship / typed signature / attestation / required answer**: `400 validation_failed` with a message per field; the form stays a draft.
- The client UI (`FormSignReview`) makes one key when the review screen opens and reuses it for every retry; if the connection drops it says the outcome is
  unknown, keeps the key, and "Sign again" cannot sign twice. "Check status" re-reads the form.

## Correction and void
A signed form is never edited. To correct it, **void** it (reason required; needs `VoidForms`) and complete a new form. The voided form keeps its signed snapshot,
flagged void; the history records who, when and why. A draft can be discarded (void) by anyone who completes forms; voiding twice changes nothing.

## Permissions (new, appended to the enum)
| Permission | Held by |
|---|---|
| `ManageFormTemplates` | Office manager, Admin |
| `CompleteForms` | Front desk, Dentist, Hygienist, Assistant, Office manager, Admin |
| `ViewSignedForms` | the above plus Billing |
| `VoidForms` (a *signed* form) | Office manager, Admin |

Every state-changing call needs CSRF; the server re-checks every permission regardless of what the UI hides.

## Audit, history and measurement
Audit entries (`FormTemplateCreated`, `FormTemplateVersionPublished`, `FormTemplateStatusChanged`, `PatientFormStarted`, `PatientFormSigned`, `PatientFormVoided`)
carry actor and time and name the template **key and version only** - never answers, the signer's name or a void reason. Those live in the form's own tables
behind `ViewSignedForms`. Signing records a `form.signed` measurement event (schema v1: category and outcome only).

## Extension seam for the document library (`ALV-010-C01`)
`PatientFormReader.SignedDocumentsAsync(patientId)` / `GET /api/patients/{id}/signed-documents` returns `SignedFormDocumentDescriptor`s: snapshot id, form id,
patient, template key, category, title, version number, signed time, integrity hash, and an `IsVoided` flag - **no answers**. A library can index or link to signed
forms through this and cannot change one: the snapshot is immutable and the descriptor is read-only (`application/vnd.alveara.signed-form+json`). Rendering a
signed form uses `GET /api/forms/{id}`.

## UI
- Patient workspace **Forms** tab (`/patients/:id/forms`, `/patients/:id/forms/:formId`) through the `patientWorkspaceTabs` extension point, shown with
  `ViewSignedForms`. A form id under the wrong patient is never shown.
- **Form Templates** administration (`/admin/form-templates`, `ManageFormTemplates`): create, publish a new version, inactivate/reactivate, version history.
- Status is always spelled in words: "Draft - unsigned", "Signed", "Void" / "Void (was signed)".

## Known limits (first release)
- Typed-name signature only; no drawn signature, no PDF rendering, no print/export, no notifications, no patient-facing (kiosk) completion - staff-assisted completion.
- No template categories beyond the four; no conditional fields. A practice can mark a template **required at check-in** (`ALV-011-C01`): that drives only the readiness cue on the visit board (complete / started / signed an earlier version / not done), never blocks a check-in and never changes a form. See `docs/SCHEDULING.md`.
- Viewing a signed form is not itself audited (only changes are).
- Template wording is plain text.
