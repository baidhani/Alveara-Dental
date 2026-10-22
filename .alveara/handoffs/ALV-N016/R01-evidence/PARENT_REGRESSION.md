# Parent regression - ALV-N016 R01

New production story (test infrastructure) with no course parent: parent regression is **N/A (no course parent)**.

The dependency `ALV-010-C01` is COMPLETE and its backend suite is the baseline: its 4,300 tests must keep their ids and outcomes (frozen Stage 2 comparison, `stage2/compare_outcomes.py`). On the focused regression, the classes of `ALV-010-C01` that are in the focused set (`DocumentSchemaTests`, `DocumentLibrarySchemaTests`, the backup and restore classes) passed on this work.
