# ALV-N022 A3-R01: demo evidence

`a3-display-real-backend` on `ecfb7f4` (fresh build, fresh database; set up through the API: a dentist, two patients, a workflow prescription finalized and amended, a diagnosis for each patient, a finalized perio chart; the missing-tooth findings recorded through the screen): 3/3, 28 s.

1. **Prescriptions tab (F-106)** - `artifacts/walkthroughs/a3-display/01-prescriptions-tab.png`: the 250 mg amendment shows "Status: Active", its instructions, "Quantity 21 capsule · Refills 0" and "This is an amendment; it replaces an earlier version (listed below)."; the 500 mg original shows "Status: Superseded: it was replaced by an amendment." and "Replaced by a later amendment (listed above)."
2. **Absent mark (F-091, CR-1)** - `02-absent-mark.png`: tooth 19 recorded Missing / Diagnosed shows "Diagnosed" with no mark; tooth 30 recorded Missing / Existing shows the ✕ and "Existing", and its accessible name ends ", not present"; the legend line and the F-095 sentence are visible. (The save line drawn over the upper row in the full-page capture is the pre-existing sticky status bar; see the handoff's shared-component finding.)
3. **Perio diagnosis link (D-1 B, F-098)** - `03-perio-diagnosis-link.png`: the picker offers Ann's "Generalized periodontitis" and not Bo's "Localized gingivitis"; the API refuses Bo's diagnosis id (400) and free text (400); the added link reads "Diagnosis: Generalized periodontitis"; the Depth / Rec. labels (F-097) are visible.

Evidence JSON: `artifacts/walkthroughs/a3-display/a3-display-e2e.json`. The ten neighbour walkthroughs keep their evidence JSON and run log in `artifacts/walkthroughs/<name>/`; their screenshots were not copied (kept with the run under `C:\tmp\a3-stage1\walk`).
