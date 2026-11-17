# ALV-N022 - scope matrix (revision 1, 2026-11-16, session CC-20261115-m7x2)

Every PKG-06 and PKG-07 register row, and where it goes. Decisions: `ALV-N022_OWNER_DECISION_RECORD.md`. Plans: `ALV-N022_PRE_IMPLEMENTATION_BRIEF.md`. Evidence: `ALV-N022_FINDING_SURVEY.md`.

**Status words:**
- **IMPLEMENT:** built in the named attempt.
- **IMPLEMENT (non-semantic):** a display or message correction only; no clinical rule changes.
- **DEFERRED:** kept in the register for future work; not built.
- **OPEN:** waiting on a decision.
- **PROPOSED:** waiting on an owner yes or no.
- **RECORD:** closed with a written explanation; no code.
- **REHOME:** misfiled; moved by the owner or reviewer.

## Implemented

| Row | Sev | What is built | Decision | Attempt |
|---|---|---|---|---|
| F-078 | S1 | The banner refreshes after any clinical write | OD-05 | A1 |
| F-080, F-112 | S2/S3 | "None reported" refused when an active item exists; legacy contradictions flagged | OD-04 | A1 |
| F-081 | S4 | One banner on the encounter page | OD-05 | A1 |
| F-084 | S2 | A normalized duplicate active allergy is refused and the user is directed to the existing item | OD-03 | A1 |
| F-085, F-111 | S2 | Encounter medication goes to the chart | OD-01/02 | A1 |
| F-086 | S2 | "Not established" reflects chart reviews made in an encounter | OD-01/05 | A1 |
| Q-5f, Q-5g, Q-5i | - | Encounter sections read the chart; "Confirm list is current" in an encounter is a chart review with provenance | OD-01/02 | A1 |
| F-103 | S2 | Completion and adoption refuse a finalized encounter | OD-06 | A2 |
| Q-5ad | - | Diagnosis attachment refuses a finalized encounter | OD-06 | A2 |
| F-106 | S2 | Prescriptions tab: status, instructions, quantity, superseded marked | OD-07 | A3 |
| F-105 (wording only) | S2 | Every "no recorded match" surface states it is not a clearance; guard test | OD-08 | A3 |
| F-067 (+F-124 dup) | S3 | Sex shown with one capitalisation everywhere (display only; stored value unchanged) | R05 | A3 |
| F-079 | S4 | Item history collapsed by default | R05 | A3 |
| F-082 | S4 | The encounter list row counts what it names (entries, chart changes, vitals) | R05 | A3 |
| F-089 | S3 | An empty alert source stays empty; nothing is invented | R05 | A3 |
| F-090 | S3 | Clearance reference is a picker from the patient's Documents; legacy free text kept and shown as "free-text reference" | R05 | A3 |
| F-092 | S3 | Withdraw wording | R05 | A3 |
| F-093 | S4 | Stale tooth message cleared when the tooth changes | PD-09d non-semantic | A3 |
| F-094 | S3 | An identical repeat says "already recorded" | PD-09d non-semantic | A3 |
| F-095 | S2 | The odontogram explains that Plan and Complete are chart statuses, not treatment-plan or completion records (no connection built) | R05 | A3 |
| F-097 | S3 | Perio site boxes labelled | R05 | A3 |
| F-098 | S2 | Perio linked-record reference becomes a picker of the patient's diagnoses; legacy free text kept and shown as such | R05 | A3 |
| F-099 (+F-104 dup) | S3 | One numbering system per screen, the practice's choice (display only; stored identity unchanged) | R05 | A3 |
| F-137 | S3 | Tooth 99 flagged on the field | R05 | A3 |
| F-199 | S4 | "has" grammar | R05 | A3 |
| F-091, Q-5v, Q-5w (display only) | S2 | The tooth summary shows each finding's recorded status ("Missing - Diagnosed"), so a Diagnosed/Planned missing finding is never shown as confirmed absence. No rule is added or changed. | PD-09d non-semantic (briefing 2.1) | A3 |
| Q-5aa | - | Diagnosis card shows its linked perio chart | R05 | A3 |
| Q-5ai | - | Tooth pre-filled from the diagnosis | R05 | A3 |
| Q-5j | - | Template management route moved outside the patient URL; permission recorded (ManageClinicalTemplates) | R05 | A3 |

## Deferred

| Row | What is deferred | Note |
|---|---|---|
| F-105 (class check) | Allergy-class seed list, drug-class, interaction, contraindication or dose logic, and any drug API | OD-08. Future feature: Medication Reference and Provider Lookup, not authorized; no patient data may leave the product. |

## Open or proposed

| Row | Status | Decision needed |
|---|---|---|
| F-091, Q-5v, Q-5w (clinical rule) | OPEN | OD-09 / PD-09d: clinical meaning; only the display correction above is built |
| F-088 | PROPOSED | OD-10: discard an empty draft |
| Q-5k | PROPOSED | OD-11 |
| Q-5o, Q-5x | PROPOSED: record as design limits | OD-12 |
| Q-5aj | Verify in A3 step 0 | If the cause is the completion's chart effect, it moves to PKG-08 with the owner's approval; otherwise it is fixed in A3 |

## Rehome

| Rows | Proposal |
|---|---|
| O-G4a..O-G4p (16) | Scheduling and board observations under ALV-N021's area: record as observations, closed or no action needed; correct the home package (OD-15) |
| O-5d..O-5g (4) | Positive observations: record, no action (OD-15) |
