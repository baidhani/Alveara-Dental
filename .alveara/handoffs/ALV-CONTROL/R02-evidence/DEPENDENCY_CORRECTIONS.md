# ALV-CONTROL R02 — Dependency Corrections (R01-01)

Every ALV record's `dependencies` array was rebuilt by transcribing the exact `**Dependencies:**` line from each item's section in `Alveara_Dental_Execution_Plan_v4.md`. Each line appears twice per item (summary block + full prompt); both copies were confirmed identical before transcription. Source line numbers below are from the copy of the plan at the time of this correction.

| Story | Plan line(s) | Transcribed dependencies |
|---|---|---|
| ALV-N001 | 375, 383 | STORY-000 |
| ALV-N002 | 456, 464 | ALV-N001 |
| ALV-001-C01 | 565, 573 | STORY-001, ALV-N001, ALV-N002 |
| ALV-N009 | 647, 655 | STORY-001, ALV-001-C01, ALV-N001, ALV-N002 |
| ALV-002-C01 | 734, 742 | STORY-002, ALV-001-C01, ALV-N002 |
| ALV-N003 | 816, 824 | ALV-001-C01, ALV-002-C01, ALV-N002 |
| ALV-N004 | 896, 904 | ALV-N002, ALV-002-C01 |
| ALV-003-C01 | 1047, 1055 | STORY-003, ALV-N003 |
| ALV-N010 | 1129, 1137 | ALV-003-C01, ALV-002-C01, ALV-N009 |
| ALV-004-C01 | 1221, 1229 | STORY-004, ALV-N003, ALV-003-C01 |
| ALV-011-C01 | 1313, 1321 | STORY-011, ALV-004-C01, ALV-N010 |
| ALV-005-C01 | 1444, 1452 | STORY-005, ALV-011-C01, ALV-002-C01 |
| ALV-N011 | 1532, 1540 | ALV-005-C01, ALV-002-C01, ALV-011-C01 |
| ALV-006-C01 | 1625, 1633 | STORY-006, ALV-005-C01, ALV-N011 |
| ALV-012-C01 | 1716, 1724 | STORY-012, ALV-005-C01, ALV-N011 |
| ALV-013-C01 | 1812, 1820 | STORY-013, ALV-006-C01, ALV-012-C01, ALV-N011 |
| ALV-N005 | 1894, 1902 | ALV-N003, ALV-002-C01 |
| ALV-015-C01 | 1982, 1990 | STORY-015, ALV-N005, ALV-013-C01 |
| ALV-007-C01 | 2126, 2134 | STORY-007, ALV-003-C01, ALV-N005, ALV-002-C01 |
| ALV-008-C01 | 2226, 2234 | STORY-008, ALV-015-C01, ALV-N005, ALV-006-C01, ALV-007-C01 |
| ALV-007-C02 | 2313, 2321 | STORY-007, ALV-007-C01, ALV-008-C01 |
| ALV-016-C01 | 2407, 2415 | STORY-016, ALV-005-C01, ALV-N011, ALV-002-C01 |
| ALV-010-C01 | 2539, 2547 | STORY-010, ALV-N010, ALV-003-C01, ALV-002-C01, ALV-N004 |
| ALV-009-C01 | 2641, 2649 | STORY-009, ALV-015-C01, ALV-004-C01, ALV-N002 |
| ALV-009-C02 | 2727, 2735 | STORY-009, ALV-009-C01, ALV-010-C01 |
| ALV-017-C01 | 2806, 2814 | STORY-017, ALV-003-C01, ALV-007-C01, ALV-010-C01 |
| ALV-018-C01 | 2891, 2899 | STORY-018, ALV-017-C01, ALV-010-C01, ALV-007-C02 |
| ALV-018-C02 | 2975, 2983 | STORY-018, ALV-018-C01, ALV-017-C01, ALV-004-C01, ALV-007-C01 |
| ALV-N006 | 3070, 3078 | ALV-007-C02, ALV-009-C01, ALV-011-C01, ALV-017-C01 |
| ALV-N007 | 3212, 3220 | ALV-005-C01, ALV-006-C01, ALV-013-C01, ALV-N011 |
| ALV-N012 | 3299, 3307 | ALV-N007, ALV-N006, ALV-015-C01, ALV-009-C01 |
| ALV-N013 | 3383, 3391 | ALV-N004, ALV-018-C01, ALV-N002 |
| ALV-N014 | 3468, 3476 | ALV-001-C01, ALV-002-C01, ALV-N004, ALV-010-C01, ALV-018-C01, ALV-018-C02, ALV-N013 |
| ALV-N008 | 3554 | ALV-N004, ALV-N006, ALV-N013, ALV-N014, ALV-018-C01, ALV-018-C02, ALV-007-C02 |

## Reviewer's four named examples — verified corrected

| Story | R01 (wrong) | R02 (corrected) | Matches reviewer expectation |
|---|---|---|---|
| ALV-001-C01 | STORY-001 | STORY-001, ALV-N001, ALV-N002 | Yes |
| ALV-N004 | ALV-N003 | ALV-N002, ALV-002-C01 | Yes |
| ALV-009-C02 | STORY-009, ALV-009-C01 | STORY-009, ALV-009-C01, ALV-010-C01 | Yes |
| ALV-N013 | ALV-N012 | ALV-N004, ALV-018-C01, ALV-N002 | Yes — no longer depends on the optional AI branch |

## Automated checks added to the generator

- **Reference validity:** every dependency ID must exist as a record ID. Result: PASS (0 unknown references across 53 records).
- **Cycle detection (DFS):** no dependency graph cycle exists. Result: PASS.
- **ALV-009-C02 → STORY-009:** explicit check retained from R01. Result: PASS.
- **ALV-N013 must not depend on ALV-N007/ALV-N012:** new explicit check added in response to R01-01. Result: PASS.
