# ALV-N022 - finding survey (read-only, draft for the owner; 2026-11-16)

Scope: R05 PKG-06 (safety and clinical-record integrity, allergy class) and PKG-07 (finalized encounters, addendum), execution plan item 59. Register: `docs/stabilization/DISPOSITION_REGISTER.json`, 62 rows with home package PKG-06/PKG-07. Code read at `2eca1a9`. No code changed, nothing run.

"Verified" = read in the current code this session. "Register" = recorded from the manual test, not yet re-read in code (to be verified first in the briefing's step 0, as R05 requires).

## 1. One root cause behind Group A (safety freshness, duplicates, medications)

The product keeps **two records of the same clinical facts**:
- the patient's **chart** (`ClinicalRecordItem`: allergies, medications, medical history, with review state), which the safety banner and the Safety tab read (`SafetyContextService` uses only chart items);
- each **encounter's own entries** (`EncounterEntry`, added with "Add allergy/medication" inside an encounter), checked for duplicates only within that encounter (`EncounterService` line ~106) and never written to the chart.

The client banner (`SafetyStrip`) refreshes only on a `alveara:safety-changed` event, which only the Safety tab sends (`SafetyPanel.tsx` lines 107, 126); encounter saves do not.

This explains, verified in code:
| Finding | Explanation |
|---|---|
| F-084 duplicate Penicillin accepted in the encounter | the encounter dedupes within itself only, not against the chart |
| F-085, F-111 medication added in an encounter missing on the Safety tab | encounter entries never reach the chart, which the Safety tab reads |
| F-080, F-112 "Reviewed - none reported" offered/kept with an active allergy | the encounter's section state looks only at the encounter's own entries |
| F-078, F-086, F-081 banner stale / "not established" after saves / shown twice | banner reads the chart and refreshes only on the Safety tab's event; the encounter page renders a second strip |

So Group A is one design change (one source of truth for allergies/medications/history, with encounters recording against the chart), not a set of small fixes. That design is part of PD-09's question and of the briefing.

## 2. Findings

| Id | Sev | Group | Status | Note |
|---|---|---|---|---|
| F-078 | S1 | A | CONFIRMED (verified: cause above) | banner stale after allergy saves |
| F-080 | S2 | A | CONFIRMED (verified) | none-reported vs active allergy - **PD-09** |
| F-081 | S4 | A | CONFIRMED (register; strip rendered on page and in encounter) | banner twice |
| F-084 | S2 | A | CONFIRMED (verified) | duplicate allergy - **PD-09** (refuse, merge or warn) |
| F-085 | S2 | A | CONFIRMED (verified) | encounter medication not on Safety tab |
| F-086 | S2 | A | CONFIRMED (register; same cause) | "not established" stays |
| F-111 | S2 | A | CONFIRMED (verified; same as F-085) | |
| F-112 | S3 | A | CONFIRMED (verified; same as F-080) | |
| F-067 | S3 | B | CONFIRMED (register) | sex shown lowercase/capitalised inconsistently; F-124 DUPLICATE |
| F-079 | S4 | B | CONFIRMED (register) | history expanded by default |
| F-082 | S4 | B | CONFIRMED (register) | "0 entries" while vitals recorded |
| F-088 | S3 | B | DESIGN_DECISION_REQUIRED (verified: no discard route) | empty drafts pile up; proposed: an empty draft (no entries, no note text, no vitals) can be discarded by its author, audited |
| F-089 | S3 | B | CONFIRMED (register; the default could not be located on the server in this pass - possibly the client form) | alert source invented as "patient" |
| F-090 | S3 | B | CONFIRMED (verified: free text `documentReference`) | clearance reference should be a picker from the patient's Documents |
| F-091 | S2 | B | DESIGN_DECISION_REQUIRED (verified: the missing-tooth rule covers Existing/Completed only) | Diagnosed/Planned missing tooth coexisting with other findings - **PD-09** |
| F-092 | S3 | B | CONFIRMED (register) | Withdraw wording |
| F-093 | S4 | B | CONFIRMED (register) | stale tooth message |
| F-094 | S3 | B | CONFIRMED (register) | identical repeat gives no message |
| F-095 | S2 | B | CONFIRMED (register) | odontogram Plan/Complete are labels only - explain or connect |
| F-097 | S3 | B | CONFIRMED (register) | perio site boxes unlabelled |
| F-098 | S2 | B | CONFIRMED (verified: free text, length check only) | perio linked-record reference |
| F-099 | S3 | B | CONFIRMED (register) | Universal vs FDI on one screen; F-104 DUPLICATE (display only; stored identity never changes) |
| F-137 | S3 | B | CONFIRMED (register) | tooth 99 not flagged |
| F-199 | S4 | B | DUPLICATE (grammar "have" -> "has") | |
| F-105 | S2 | C | CONFIRMED (verified: `AllergyCheck` is a name comparison) | allergy-class check - **PD-12** for who owns the class data |
| F-106 | S2 | C | CONFIRMED (register) | Prescriptions tab shows no status/instructions/quantity; superseded version not marked |
| F-103 | S2 | PKG-07 | CONFIRMED (verified: completion checks the encounter's patient, never that it is a draft) | **PD-10** addendum/amendment route |

## 3. Open questions (Q-5*) - proposed handling

| Id | Question | Proposed |
|---|---|---|
| Q-5f, Q-5g, Q-5i | encounter sections and the chart: "needs attention" although the chart has the item; banner after "Confirm list is current" | answered by the one-source design in section 1 |
| Q-5j | templates under a patient URL; who may manage them | record: practice-wide, ManageClinicalTemplates; move the route outside the patient (small) |
| Q-5k | the author must acknowledge their own alert | owner choice (recommend: the author's own alert counts as acknowledged) |
| Q-5o | template chooser disappears after the first template | record as design limit, or allow a second template with confirmation |
| Q-5v, Q-5w | implant vs missing tooth | **PD-09** (clinical meaning) |
| Q-5x | perio: typing 0 on all six sites | record: deliberate (empty is not 0); offer "all 0" per tooth only if the owner wants it |
| Q-5aa | diagnosis card does not show its linked perio chart | small display fix |
| Q-5ad | diagnosis attached to a finalized encounter | **PD-10** (same rule as F-103) |
| Q-5ai | tooth not prefilled from the diagnosis | small fix |
| Q-5aj | completing L3000 on tooth 14 M shows "none recorded" on Mesial | verify: completion chart effect vs surface (may belong to PKG-08) |

## 4. Rows that do not belong here

O-G4a .. O-G4p (16 scheduling/visit-board observations) and O-5d .. O-5g (4 positive clinical observations) carry home package PKG-06 in the register. The O-G4 rows are about the calendar, booking and the board (A2's area, now closed) and are observations, not defects; propose recording them as observations closed by ALV-N021 or not requiring action, and correcting their home package in the register (owner/reviewer call).

## 5. What is needed before the briefing

The owner decisions PD-09, PD-10 and PD-12 (`docs/design/ALV-N022_DECISION_REQUEST.md`), then a pre-implementation briefing for independent review, as for ALV-N021.
