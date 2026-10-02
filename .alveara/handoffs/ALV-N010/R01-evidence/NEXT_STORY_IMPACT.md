# ALV-N010 R01 — Next-story impact

Next scheduled item after this story is reviewed and closed: **item 14 — `STORY-004`** (a course story, executed in the portal with its own prompt — not started here). Later stories that depend on `ALV-N010`: `STORY-011`/`ALV-011-C01` (check-in readiness surfaces incomplete/complete required forms), `ALV-010-C01` (document library) and any story needing a signed consent. Details in `docs/FORMS_AND_CONSENTS.md`.

## What later stories can now rely on
- **Template versions are immutable and signed snapshots are immutable** (application guard + database triggers). Never write code that updates `SignedFormSnapshots` or `FormTemplateVersions`; a future migration that must alter those tables has to drop and recreate `TR_SignedFormSnapshots_Immutable` / `TR_FormTemplateVersions_Immutable` deliberately.
- **A signed form is addressable**: `SignedFormSnapshot.Id` (one per `PatientForm`), with `SnapshotHash`, `TemplateKey`, `Category`, `TemplateVersionNumber`, `SignedAtUtc`. Reference it by id; do not copy the answers elsewhere.
- **The read seam for `ALV-010-C01`**: `PatientFormReader.SignedDocumentsAsync(patientId)` / `GET /api/patients/{id}/signed-documents` → `SignedFormDocumentDescriptor` (no answers; `IsVoided` flag; content type `application/vnd.alveara.signed-form+json`). Show a signed form with `GET /api/forms/{id}`.
- **Check-in readiness (`STORY-011`/`ALV-011-C01`)** can ask "does this patient have a current signed (not void) form of template key X / category Y?" from `PatientForms` + `SignedFormSnapshots` (`Status = 'Signed'`, a snapshot exists). There is **no required-forms list yet**: deciding which forms a visit requires is that story's scope; keep it in its own table so these completed tables are not modified. Viewing a form never marks it complete or signed.
- **Signer model**: free-text name + relationship (Self, Parent, Legal guardian, Spouse, Legal representative, Other + description); not linked to a registered patient. A story that needs to link a signer to a patient/guarantor may add a nullable column in its own migration but must not change existing snapshots.
- **Patterns to reuse**: one atomic save for the change + history event + audit entry + idempotency receipt (`PatientFormService.SignAsync`); `Idempotency-Key` required for consequential commands; the signature UI keeping one key across retries and saying so when an outcome is unknown; a draft pinned to a template version; `FormStatusBadge` (status in words); `FormFieldInputs`/`FormAnswers`.
- **Permissions**: `ManageFormTemplates`, `CompleteForms`, `ViewSignedForms`, `VoidForms` (grants in `docs/FORMS_AND_CONSENTS.md`).
- **Measurement**: `form.signed` (schema v1).
- **The patient workspace tab extension point** now has a real second consumer (Forms), which is evidence for Gate B row B3's "later modules extend" clause.

## Open items / unresolved decisions that touch later work
- **F1 (`ALV-N001` toast, 4.45:1) and F2 (`ALV-002-C01` conflict-banner title, 3.46:1)** remain open (see the `ALV-003-C01` R02 review). The forms and template editors render the shared conflict banner inside `.alv-workspace__form`/`__panel`, where the existing local override applies; any page outside those containers still shows the failing title colour. They must be corrected through reopened-story attempts before Gate B is evaluated.
- No audit of *reading* a signed form; add it before the document library exposes bulk access if the practice's privacy policy requires it.
- Gate B rows B1 and B3 have supporting evidence from `ALV-003-C01` and this story but remain `NOT YET EVALUABLE`.

## Prompt changes relevant to the next item
None required. `STORY-004` is a course story (portal prompt); `STORY-011`/`ALV-011-C01` should read this file for the check-in readiness hook.
