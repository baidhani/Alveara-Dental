# Demo evidence - ALV-013-C01 R01

The visible workflow was run un-mocked: a real Chromium, the real Vite proxy, the real `Alveara.Api` and a fresh migrated LocalDB (`e2e/diagnoses-structure-real-backend.spec.ts`, 12 of 12 on the final tree; the screenshots below are from a standalone run of the same code). Real encounters, a real odontogram finding and a real periodontal chart per patient. Test data only; no real patient data.

| Screenshot | What it shows |
|---|---|
| `01-no-coding.png` | a diagnosis recorded with no coding, source or region: stored as manual and uncoded |
| `02-structured-form.png` | the record form with an oral region, a coding system and code, and a source with a note filled in; the safety strip in view |
| `03-structured-recorded.png` | the recorded diagnosis with its context chips (region, coding, source) and no claim that the code was checked |
| `04-structure-problems.png` | a wrong code refused on screen: the problem list linked to the box, nothing sent, what was typed kept |
| `05-amend-needs-reason.png` | the amend form refusing to save without a reason, listing the problem |
| `06-amendment-history.png` | the amended diagnosis and its history: the first row still shows the replaced tooth; the amendment row shows who, when, why and the new region and coding; the treatment-plan reference unresolved in every row |
| `07-resolved.png` | a diagnosis marked resolved with a reason, still on the list and labelled Resolved |
| `08-link-picker.png` | the link picker offering only this patient's own finding and chart |
| `09-linked.png` | the linked record shown with who linked it and when |
| `10-stale-amendment.png` | an amendment from a stale screen: the shared conflict message, what was typed kept |
| `11-assistant-read-only.png` | the assistant sees the chips, links and history with no form and no action buttons |
| `12-tablet.png` | the screen at 768 px, no horizontal page scroll |

## States covered
No coding at all; coding, source and region round trip; structure problems refused on screen and by the API (each named); amendment with a reason, the earlier values kept in the history, an amendment that changes nothing being quiet; resolve and reactivate with a reason, a resolved diagnosis staying on the list; links to a real finding and a real chart, a repeat adding nothing, another patient's record and a missing one refused in the same words; the treatment-plan reference refused any state but Unresolved on record, correct and amend; a stale amendment; a withdrawn diagnosis refusing every structural change; the audit log holding every new event with user and time and none of what was diagnosed or coded; every role (hygienist amends; assistant reads and is refused; front desk and billing denied; anonymous 401; no CSRF refused); axe scans with the forms open in light and dark; the 768 px tablet layout.

`artifacts/walkthrough/diagnoses-structure-e2e.json` holds the machine-readable results (axe counts per scan, the audit counts, the history change types).
