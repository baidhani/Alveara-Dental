# Demo evidence — ALV-005-C01 R01

The visible workflow was run un-mocked: a real Chromium, the real Vite proxy, the real `Alveara.Api` and a fresh migrated LocalDB (`e2e/clinical-companion-real-backend.spec.ts`, 9 of 9, run twice on the final code). Test data only; no real patient data. Raw output and the JSON results are in `artifacts/companion-walkthrough/`.

| Screenshot | What it shows |
|---|---|
| `01-record-captured.png` | the Clinical tab's record: four sections, items with status words and "Added by <name> on <date>", "Needs review" and "Not reviewed" in words |
| `02-record-history.png` | an allergy's history: Added, then Status changed to Resolved with the reason and the second clinician's name |
| `03-none-known.png` | "none known" stated by a clinician, with who and when, and no item created |
| `04-record-stale-edit.png` | the shared conflict banner after another clinician changed the item; typed text kept |
| `05-templates.png` | the note templates page (a dentist's view) |
| `06-template-applied.png` | an encounter with the template applied: starter text, required notes |
| `07-review-required-notes.png` | the signing review naming each required note still unwritten; Sign and Finalize disabled |
| `08-notes-and-vitals.png` | SOAP, progress and treatment notes (saved, attributed), a voided reading kept and marked, a correct reading |
| `09-signed-locked.png` | a signed note: locked, with the signer named and "Unsign to keep editing" |
| `10-amendment-timeline.png` | a finalized note: original untouched, "Original finalized ... by ...", an addendum "Amends: Plan" with author and time |
| `11-note-stale.png` | a stale note save refused with the conflict banner |

(The sticky save indicator appears mid-page in the full-page captures; that is an artefact of capturing a scrolled page.)

## States covered
Loading, empty ("Not reviewed", no encounters, no notes), error (a refused save, a dropped connection, a failed load), permission (front desk denied by API and absent from the UI; an assistant reads with zero controls), conflict (stale item and stale note), signed (locked), finalized (read-only), voided (kept, marked). Accessibility: 18 axe scans (record, record with the remove form open, record conflict banner, template form, templates list, encounter notes and vitals, signing review, signed note, finalized note with timeline — each in light and dark): **0 critical/serious**. Keyboard: an encounter is finalized with the keyboard only (focus + Enter on the review and finalize buttons). Not run: tablet width.
