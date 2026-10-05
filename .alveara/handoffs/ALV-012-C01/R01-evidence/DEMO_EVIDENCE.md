# Demo evidence - ALV-012-C01 R01

The visible workflow was run un-mocked: a real Chromium, the real Vite proxy, the real `Alveara.Api` and a fresh migrated LocalDB (`e2e/perio-sessions-real-backend.spec.ts`, 12 of 12 on the final tree). Test data only; no real patient data. Raw output, the JSON results and the screenshots are in `artifacts/walkthrough/`; STORY-012's own unchanged walkthrough's run is in `artifacts/parent-regression-walkthrough/`.

| Screenshot | What it shows |
|---|---|
| `01-first-site.png` | a chart in progress at its first site, with the cursor in the depth box and the safety strip in view |
| `02-mid-entry.png` | mid-entry: the tooth strip with each tooth's state in words, the current site in large type |
| `03-tooth-not-charted.png` | a tooth marked not charted, shown in the strip with 'Chart this tooth' to put it back |
| `04-missing-tooth-skipped.png` | a tooth the odontogram records as missing, skipped and listed, its chip disabled |
| `05-refused-entries-kept.png` | the server refused a tooth that became missing: the entries are kept, the problems listed with tooth and site, the cursor on the entry to correct |
| `06-conflict.png` | a draft changed by someone else: the shared conflict banner, the typed entry kept |
| `07-all-entered.png` | all 192 sites entered from the keyboard |
| `08-comparison.png` | a chart compared with the one before it: the trend in words, the figures then and now, the changed sites with cue words, the teeth that changed, and the links |
| `09-live-comparison.png` | the chart in progress compared live with the last finalized chart |
| `10-tablet-entry.png` | the entry screen at 768 px, no horizontal page scroll |

## States covered
No chart in progress; the first site; keyboard entry with the six sites reading differently; pus, plaque, mobility and furcation; a tooth not charted; a tooth missing in the odontogram skipped and refused; a refused save keeping what was typed; a stale draft as a conflict; a whole mouth typed from the keyboard and finalized; the comparison (a saved chart and the live draft), links; the audit log; every role (dentist, hygienist, assistant read-only, front desk and billing denied, anonymous, no CSRF token); axe light and dark on the entry screen, with a problem shown, the comparison and the tablet layout (0 critical or serious).
