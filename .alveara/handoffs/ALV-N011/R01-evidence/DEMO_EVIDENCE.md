# Demo evidence — ALV-N011 R01

The visible workflow was run un-mocked: a real Chromium, the real Vite proxy, the real `Alveara.Api` and a fresh migrated LocalDB (`e2e/safety-real-backend.spec.ts`, 11 of 11 on the final code). Test data only; no real patient data. Raw output and the JSON results are in `artifacts/safety-walkthrough/`.

| Screenshot | What it shows |
|---|---|
| `01-empty-header.png` | the header strip for a patient with nothing on file: "No alerts, active allergies or current medications are recorded." and each "not been reviewed ... Nothing listed here does not mean none." gap |
| `02-empty-safety.png` | the Safety tab for the same patient: "This is not a statement that there are none." and "Not established" |
| `03-record-entries.png` | allergy and medication read from the clinical record, with source, severity words, and "Change it in the clinical record" (no buttons) |
| `04-unacknowledged.png` | a second clinician's header: "1 alert you have not acknowledged yet." |
| `05-acknowledged-still-active.png` | after acknowledging: "It is still active - acknowledging does not resolve it."; the status stays Active; the strip count follows |
| `06-resolved-with-reason.png` | a resolved alert kept in the resolved list with who resolved it and the reason |
| `07-clearance-received-no-document.png` | "Received - not yet resolved" and "Supporting document not yet attached." |
| `08-clearance-resolved.png` | the clearance resolved with a reason, by name |
| `09-encounter-strip.png` | the safety strip at the top of an open encounter, above the documentation sections |
| `10-board-indicator.png` | the live visit board: "Safety alert on file" / "Clearance open" on two cards and nothing on the third |
| `11-stale-edit.png` | a stale alert edit refused with the shared conflict banner |

(The sticky save indicator can appear mid-page in a full-page capture; that is an artefact of capturing a scrolled page.)

## States covered
Loading, empty (nothing recorded + gaps), error ("could not be loaded, so do not assume there is none"), permission (front desk, practice manager and billing denied by the API and drawn nothing; an assistant reads and acknowledges with no change controls), conflict (stale edit, stale acknowledgement), acknowledged (still active), resolved, reopened, clearance requested / received-without-document / document-attached / resolved / cancelled. Accessibility: 12 axe scans (the safety details, with forms open, with clearances, the encounter with the strip, the board with indicators, the conflict banner - each in light and dark): **0 critical/serious**. Keyboard: an alert is acknowledged with the keyboard only. Not run: tablet width.
