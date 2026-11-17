# ALV-N022 - scope matrix (revision 2, 2026-11-16, session CC-20261115-m7x2)

Revision 2 answers review BRIEFING-R01 BR-3, BR-4 and BR-6. Decisions: `ALV-N022_OWNER_DECISION_RECORD.md` (rev. 2). Plans: `ALV-N022_PRE_IMPLEMENTATION_BRIEF.md` (rev. 2). Evidence: `ALV-N022_FINDING_SURVEY.md`.

## Status words

- **PLANNED:** planned for implementation in the named attempt. Nothing here is implemented.
- **Verified:** "code" means read in the code at `2eca1a9`. "register" means reported from the manual test and not yet verified in code; it must pass the step-0 gate (briefing section 3) before it is built.
- **DEFERRED:** kept in the register; not built.
- **OPEN:** awaiting a decision.
- **PROPOSED:** awaiting the owner; not built and not tested.
- **REHOME:** misfiled.

**Gate:** the attempt stops and returns a briefing addendum for any PLANNED row whose step-0 verification is NOT_REPRODUCED, belongs to another package, or needs a new rule, schema, permission or API beyond this briefing.

## Planned for implementation

| Row | Sev | Verified | Planned change | Decision | Attempt |
|---|---|---|---|---|---|
| F-078 | S1 | code | Banner refreshes after any clinical write | OD-05 | A1 |
| F-080, F-112 | S2/S3 | code | "None reported" refused when an active item exists; contradictions flagged | OD-04 | A1 |
| F-081 | S4 | register | One banner on the encounter page | OD-05 | A1 |
| F-084 | S2 | code | Normalized duplicate **active allergy** refused, user directed to the existing item (allergies only) | OD-03 | A1 |
| F-085, F-111 | S2 | code | Encounter medication goes to the chart | OD-01/02 | A1 |
| F-086 | S2 | register | "Not established" reflects chart reviews made in an encounter | OD-01/05 | A1 |
| Q-5f, Q-5g, Q-5i | - | register | Encounter allergy, medication and medical-history sections read the chart; "Confirm list is current" in an encounter is a chart review with provenance | OD-01/02 | A1 |
| F-103 | S2 | code | Completion and adoption refuse a finalized encounter | OD-06 | A2 |
| Q-5ad | - | register | Diagnosis attachment refuses a finalized encounter | OD-06 | A2 |
| F-106 | S2 | register | Prescriptions tab: status, instructions, quantity, superseded marked | OD-07 | A3 |
| F-105 (wording only) | S2 | code | "No recorded match" never shown as a verdict; guard test | OD-08 | A3 |
| F-067 (+F-124 dup) | S3 | register | Sex displayed consistently (stored value unchanged) | R05 B | A3 |
| F-079 | S4 | register | Item history collapsed by default | R05 B | A3 |
| F-082 | S4 | register | Encounter list row counts what it names | R05 B | A3 |
| F-089 | S3 | register | Empty alert source shown as "not recorded"; nothing invented | R05 B | A3 |
| F-090 | S3 | code (free text) | **Picker scope:** choose one of the patient's own non-deleted Documents. The stored reference becomes that document's id. Legacy free text is kept and shown as "free-text reference (legacy)". No cross-patient search, no upload from the picker. Whether a schema column is needed is a step-0 item. | R05 B | A3 |
| F-092 | S3 | register | Withdraw wording | R05 B | A3 |
| F-093 | S4 | register | Stale tooth message cleared | PD-09d display | A3 |
| F-094 | S3 | register | Identical repeat says "already recorded" | PD-09d display | A3 |
| F-095 | S2 | register | **Explain only:** a text on the odontogram saying Plan and Complete are chart statuses, not treatment-plan or completion records. No connection is built. If step 0 shows anything other than labels, it stops for an addendum. | R05 B | A3 |
| F-097 | S3 | register | Perio site boxes labelled | R05 B | A3 |
| F-098 | S2 | code (free text) | **Picker scope:** choose one of the same patient's diagnoses. Legacy free text is kept and shown as legacy. No other record types. Schema need is a step-0 item. | R05 B | A3 |
| F-099 (+F-104 dup) | S3 | register | One numbering system per screen (display only) | R05 B | A3 |
| F-137 | S3 | register | Tooth 99 flagged on the field | R05 B | A3 |
| F-199 | S4 | register | "has" grammar | R05 B | A3 |
| F-091, Q-5v, Q-5w (display only) | S2 | code (rule) / register (display) | Each tooth finding shows its recorded status. The absent glyph is shown only for Existing/Completed. No rule change. | OD-09 non-semantic | A3 |
| Q-5aa | - | register | Diagnosis card shows its linked perio chart | R05 B | A3 |
| Q-5ai | - | register | Tooth pre-filled from the diagnosis | R05 B | A3 |
| Q-5j | - | register | Template management route moved outside the patient URL | R05 B | A3 |

## Not changed (BR-3, BR-4)

| Item | Note |
|---|---|
| Dental history | Stays on its existing encounter-entry model and behaviour. It is not moved to the chart in ALV-N022 (no decision covers it). |
| Duplicate rules for medications and medical history | Unchanged. The new normalized refusal is for allergies only. |

## Deferred

| Row | Note |
|---|---|
| F-105 (allergy-class check), PD-12 | OD-08. Deferred, not removed. The plan, R05 and the register are amended. It is a release limitation. Follow-on: the Medication Reference and Provider Lookup proposal, not authorized. |

## Open or proposed (not built, not tested)

| Row | Status |
|---|---|
| F-091, Q-5v, Q-5w (clinical meaning) | OPEN (OD-09) |
| F-088 | PROPOSED (OD-10) |
| Q-5k | PROPOSED (OD-11) |
| Q-5o, Q-5x | PROPOSED (OD-12) |
| Signed drafts and new care | PROPOSED (OD-13) |
| Legacy "add to chart" flow | PROPOSED (OD-14) |
| Q-5aj | Step 0. If the cause is the completion's chart effect (PKG-08), an addendum and the owner's approval are needed. |

## Rehome

O-G4a..O-G4p and O-5d..O-5g: OD-15, proposed.
