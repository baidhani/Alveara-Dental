# Demo evidence - ALV-006-C01 R01

The visible workflow was run un-mocked: a real Chromium, the real Vite proxy, the real `Alveara.Api` and a fresh migrated LocalDB (`e2e/odontogram-longitudinal-real-backend.spec.ts`, 11 of 11 on the final code). Test data only; no real patient data. Raw output, the JSON results and the screenshots are in `artifacts/odontogram-longitudinal-walkthrough/`; the unchanged original `STORY-006` walkthrough's run is in `artifacts/parent-regression-odontogram-walkthrough/`.

| Screenshot | What it shows |
|---|---|
| `01-mixed-chart.png` | one patient with findings on a permanent and a primary molar, drawn as the mixed chart (four arches, 52 teeth); the tooth detail of a primary incisor offering Incisal and Facial surfaces |
| `02-tooth-absent.png` | the server's refusal, in words, of a finding on a tooth recorded as missing; an implant is allowed |
| `03-catalogue-add.png` | a dentist adds a condition type (the catalogue and its form) |
| `04-catalogue-retired.png` | the condition retired with a reason; it is listed as Retired and absent from the record form, while the old finding still reads as it did |
| `05-tooth-history.png` | a tooth's history: recorded, planned, completed, a second finding withdrawn with its reason, and entered again - oldest first with who and when |
| `06-links.png` | links to a diagnosis, a treatment plan and a procedure beside the finding and in the tooth's history |
| `07-catalogue-conflict.png` | a stale condition change refused with the conflict banner naming a condition type |
| `08-history-and-catalogue.png` | the tooth history and the catalogue's add form open together (the state axe scanned) |
| `09-tablet-mixed.png` | the mixed chart at 768 px: arches wrap into rows, no horizontal scroll |

## States covered
Loading, empty chart ("nothing recorded", never healthy), the catalogue failing to load (findings cannot be recorded), a failed history load, permission (a hygienist and an assistant read the catalogue and the tooth timeline but see no catalogue controls and get 403 from the API; front desk and the practice manager get nothing), conflict (stale catalogue change and, in the parent walkthrough, stale finding change), refused entries with the field named, retired conditions, absent teeth, links, and the three dentition views. Accessibility: axe scans of the mixed chart, a tooth's history with its links, the catalogue with its add form and the tablet layout, each in light and dark: **0 critical/serious**. Keyboard: arrow keys across the four arches without selecting, Enter selecting. **Tablet:** the mixed chart was run at 768 px with no horizontal scroll (the reviewer had asked for tablet-width validation of clinical screens; the odontogram now has it, the earlier clinical and safety screens still do not).
