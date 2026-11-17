# ALV-N022 A1-R01: demo evidence

Real Chromium -> Vite proxy -> Alveara.Api -> SQL Server, fresh database per spec, implementation `6414ffa`.

## encounter-chart-real-backend (new; 1/1, 27 s)
A dentist starts an encounter for a new patient:
1. Adds "  Penicillin  " in the encounter: saved to the chart as "Penicillin"; the one banner changes to "1 active allergy" without a reload.
2. Resolves it from the encounter (status chosen): the banner drops the active allergy.
3. Adds "penicillin " again: "Penicillin is already on the chart as resolved. Nothing was added." (`01-resolved-match.png`, axe light and dark: no critical/serious). Cancel: the API still shows one Resolved item; the typed text stays in the form.
4. Sends it again and chooses "Reactivate or update the existing item": the status select starts empty and Save is disabled; Active is chosen and saved; the banner shows "1 active allergy" (`02-reactivated.png`).
5. Leaving with the form still open asks to discard (dismissed: stays); after closing the form, the banner's "Open safety details" opens the Safety tab listing Penicillin with no page load (`03-safety-tab.png`).
6. API: one chart record; this encounter's provenance Added, StatusChanged, StatusChanged.
Evidence JSON: `artifacts/walkthroughs/encounter-chart/encounter-chart-e2e.json`.

## clinical-real-backend (rewritten; 9/9, 57 s)
Documents an encounter (medical history, allergy, medication to the chart; dental on the encounter) - `02-documented.png`; incomplete-documentation review (`03-review-incomplete.png`), then dental "none reported" and allergies/medications "confirmed current" (none reported is not offered and the API refuses it); finalize, addendum, concurrency, keyboard, scope, audit (encounter and chart audit, no clinical text).

## safety-real-backend (rewritten test 9; 11/11, 51 s)
One banner on the encounter: the patient header's, above the documentation (`09-encounter-strip.png`).

## clinical-companion-real-backend (unchanged; 9/9, 62 s)
Passes unchanged on the final tree.
