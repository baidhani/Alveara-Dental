# ALV-N024 R02: next-story impact

As `R01-evidence/NEXT_STORY_IMPACT.md`, plus:
- **51142 is active-only:** an inactive guarantor, household member or recorded-responsible party can no longer be recorded as a payer (in the database and the service). Imports or merges that insert payments with a payer must respect this. Stored rows are never re-checked.
- **CSS:** `.alv-dx__form label.alv-bill__choose` overrides the shared column layout for that one label. Surveyed (2026-11-19, a search of the client source): the only other checkbox label inside an `.alv-dx__form` is the Apply form's, which has the same class and so the same fix. The other checkboxes (treatment plan, diagnoses, perio) are not inside `.alv-dx__form` labels.
