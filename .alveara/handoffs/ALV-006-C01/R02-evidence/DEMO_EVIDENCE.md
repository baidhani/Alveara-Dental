# Demo evidence - ALV-006-C01 R02

The visible workflow was run un-mocked: a real Chromium, the real Vite proxy, the real `Alveara.Api` and a fresh migrated LocalDB (`e2e/odontogram-longitudinal-real-backend.spec.ts`, 11 of 11 on the R02 tree; the `TOOTH-STATE VALIDATION` step was extended for the review finding). Test data only; no real patient data. Raw output, the JSON results and the screenshots are in `artifacts/odontogram-longitudinal-walkthrough/`; the unchanged original STORY-006 walkthrough's run is in `artifacts/parent-regression-odontogram-walkthrough/`.

| Screenshot | What it shows |
|---|---|
| `01-mixed-chart.png` | one patient with findings on a permanent and a primary molar, drawn as the mixed chart (four arches, 52 teeth); the tooth detail of a primary incisor |
| `02-tooth-absent.png` | the refusal, in words, of a finding on a tooth recorded as missing; an implant is allowed |
| `02b-tooth-has-findings.png` | **(R02)** the refusal when a planned missing tooth is completed while another finding stands on it: 'Not saved: This tooth has other active findings ... Nothing was changed.' |
| `03-catalogue-add.png` | a dentist adds a condition type |
| `04-catalogue-retired.png` | the condition retired with a reason; listed as Retired, absent from the record form, the old finding still reads as it did |
| `05-tooth-history.png` | a tooth's history: oldest first with who and when, withdrawn findings included |
| `06-links.png` | links to a diagnosis, a treatment plan and a procedure beside the finding and in the history |
| `07-catalogue-conflict.png` | a stale condition change refused with the conflict banner naming a condition type |
| `08-history-and-catalogue.png` | the tooth history and the catalogue form open together (the state axe scanned) |
| `09-tablet-mixed.png` | the mixed chart at 768 px, no horizontal scroll |

## States covered
Everything R01's demo covered (loading, empty, failed loads, permission, conflict, retired conditions, links, the three dentition views, axe light and dark, tablet), plus, for R02: a missing tooth taking nothing else; a tooth that **cannot become missing beside other findings** (refused on screen and over HTTP with the version unchanged, then completed after the other finding is withdrawn with a reason, after which the tooth takes nothing further); and four dentist-versus-hygienist races over HTTP on one tooth each, exactly one request winning each time and the chart never holding an absence beside an ordinary finding.
