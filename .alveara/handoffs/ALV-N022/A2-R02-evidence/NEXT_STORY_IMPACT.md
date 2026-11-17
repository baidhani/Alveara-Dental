# ALV-N022 A2-R02: next-story impact

As A2-R01 (`A2-R01-evidence/NEXT_STORY_IMPACT.md`), plus:
- One more migration (`20261117160917_AddDiagnosisFinalizedEncounterGuard`) for every database upgrade, including the Windows installer path (ALV-N013) and the ALV-N028 consolidated run (`MigrationTests` from an empty database was not run here).
- Any future code that inserts diagnoses (an import, a merge that copies rows) must place them on a non-finalized encounter or be designed around trigger 51287.
