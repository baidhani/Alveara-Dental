# ALV-008-C02 R02 - Demo evidence

The correction has no screen of its own. The adoption walkthrough (`src/alveara-client/e2e/completion-adoption-real-backend.spec.ts`: a real Chromium, the real Vite proxy, the real `Alveara.Api` and a fresh LocalDB database) was rerun on the R02 build, and it still ends with a reconciliation of all zeros, now judged by the recorded finding version. 8 of 8 passed. The screenshots in `artifacts/walkthrough/` are from this run and were viewed before being cited; they are the same seven states as R01's (`../R01-evidence/DEMO_EVIDENCE.md` describes each):

| # | Screenshot | What it shows |
|---|---|---|
| 01 | `01-legacy-completions-waiting.png` | Three legacy completions waiting; the old controls absent; "Adopt earlier completions..." offered |
| 02 | `02-adopt-form-effects-and-gaps.png` | The adopt form saying what adopting will do and naming every missing field |
| 03 | `03-adopted-in-history.png` | After the adoption: the clinical history reads "Performed by ... charged $140.00 ... chart finding completed" |
| 04 | `04-failed-adoption-records-nothing.png` | With the audit log unwritable: "Nothing was recorded", the form keeps what was typed |
| 05 | `05-stale-second-screen.png` | The out-of-date second screen is told and records nothing |
| 05b | `05b-revised-original-kept.png` | After a revision the original revision and the completed care are kept |
| 06 | `06-gate-d-reconciled.png` | The end state, with the reconciliation (API) all zero |

The adoption of the filling moves the tooth 16 finding to Completed (the chart, the history and the ledger all follow, as in R01) and, new in R02, the record stores the version that shows it Completed; the walkthrough does not display that number (it is evidence, not a screen), so its effect is shown by `GET api/completion/reconciliation` being `clean` at the end.
