# ALV-N022 A1-R01: next-story impact

- **A2 (finalized-encounter completion; not authorized yet):** builds on the encounter read model here; completion/adoption/diagnosis pickers must read chart-backed sections through `source`, `chartItems` and `recordedHere`, not `entries`, for the moved kinds.
- **A3 (display findings, F-106, PD-09d; not authorized yet):** the encounter allergy section is now the chart; any allergy display row in A3's matrix should use `ChartSectionPanel`'s data.
- **ALV-N028 (consolidated regression):** owes the full suite over every class listed in the ledger rows for this attempt, every walkthrough that opens an encounter, and the mocked Playwright suite.
- **Deployment (ALV-N013 installer path):** run the allergy-collision preflight on the deployment database first (owner decision). Existing allergy names that differ only by inner spacing remain separate records; the preflight should list them for a clinician.
- **Any consumer of `EncounterSection.entries` for Allergy/Medication/MedicalHistory** (reports, exports, prints) now sees only pre-A1 entries there; the chart is the source. A grep of client consumers at package time found one: `FinalizeReview.tsx` shows "(n)" from `entries.length`, so chart sections show their status without a count (known display gap, not fixed in A1). The encounter list's `entryCount` was updated server-side to include versions made in the encounter. Server-side exports and prints were not audited in A1.
- **UX follow-up (owner's call):** close the add form when the server points to an existing item.
