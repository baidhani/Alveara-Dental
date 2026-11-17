# ALV-N022 - owner decision record (revision 1)

- **Story:** ALV-N022 (execution item 59): R05 PKG-06 (safety and clinical-record integrity) and PKG-07 (finalized encounters, addendum).
- **Date:** 2026-11-16. **Session:** CC-20261115-m7x2.
- **Source of the decisions:** the owner's answers of 2026-11-16 to `docs/design/ALV-N022_DECISION_REQUEST.md`, and the owner's planning instruction of the same day. The decision request is superseded by this record wherever they differ.
- **Status:** decisions recorded as given by the owner. Items marked **OPEN** or **PROPOSED** are not decided and are not built.
- **Owner approval of this record:** [selected] Approved as written - ___Firas Baidhani__ (date) _11/16/2026_

## 1. Decided

| Id | Decision | Acceptance criterion | Attempt |
|---|---|---|---|
| OD-01 (PD-09a) | The patient chart (`ClinicalRecordItem`) is the **single current source** for allergies, medications and medical history (and dental history, which shares the same model). | After any add, change or status change made from an encounter, the Safety tab, the safety banner, the chart and the allergy check on prescribing all show the same items, without a page reload. | A1 |
| OD-02 (PD-09a) | Encounter actions **update the chart** and keep an **immutable "recorded/reviewed in this encounter" provenance** entry. | Every chart change or section review made from an encounter appends a version or event carrying that encounter's id. The encounter shows what was recorded or reviewed in it, and a finalized encounter's list never changes afterwards. | A1 |
| OD-03 (PD-09b) | An **exact normalized duplicate active allergy is refused**, and the user is directed to the existing item to amend it. **Never silently merge records.** Normalization: trim; collapse inner whitespace; compare without regard to case. | Adding "penicillin" or " Penicillin  " while "Penicillin" is active gives 409 `duplicate_item` naming the existing item and links to it. No second item is created and no existing item is changed. A retried identical add replays the existing item. | A1 |
| OD-04 (PD-09c) | "None reported" is **unavailable when an active item exists** in that section. **Old contradictions are visibly flagged, not erased.** | The action is not offered, and the API refuses it with 409 `section_has_active_items`. A stored legacy "none reported" or "none known" that contradicts an active item is shown as a contradiction. Both records are kept. | A1 |
| OD-05 | Stale and duplicate safety-banner behaviour is resolved (F-078, F-081, F-086). | One banner per page. It refreshes after every successful clinical-record write from any screen. | A1 |
| OD-06 (PD-10 A) | A **finalized encounter accepts no new clinical care**: no procedure completion, no adoption of a completion, no diagnosis attachment. Later care belongs in a new draft encounter. Corrections to the finalized narrative stay **append-only text addenda** (the existing route). | Each of these server paths refuses a finalized encounter with 409 `encounter_finalized` and writes nothing: completion, adoption, diagnosis create, and any re-link. The pickers list only draft encounters. The addendum route is unchanged. | A2 |
| OD-07 (F-106) | Prescription display improvements stay in scope. | The Prescriptions tab shows each prescription's status, instructions and quantity, and marks a superseded version as superseded. | A3 |
| OD-08 (F-105 / PD-12) | The allergy-class feature is **deferred, not removed**. ALV-N022 adds **no** allergy-class seed list. It adds no drug-to-drug, contraindication, dose, compatibility or prescribing-decision logic, and no public or commercial drug API. Only the existing direct recorded-name allergy warning stays. The absence of a direct-name warning is **never** presented as the medication being safe, compatible, approved or clear. | Every surface that shows the allergy-check result for "no recorded match" says it is a name comparison only, not a clearance. A test fails if that text, or any text near the result, contains "safe", "compatible", "approved", "clear" or "cleared" used as a verdict. F-105 is recorded as DEFERRED in the register with the future-feature note (section 3). | A3 |

## 2. Open or proposed (not decided; not built until decided)

| Id | Item | Status | Note |
|---|---|---|---|
| OD-09 (PD-09d) | Clinical meaning of a Diagnosed/Planned "missing tooth", extraction intent, and implant-to-missing relationships (F-091, Q-5v, Q-5w) | **OPEN** | The owner rejected "planned for extraction" as an inferred meaning. The owner's direction for a future decision: only Existing/Completed establishes absence; a planned extraction belongs to the treatment plan; implant/missing relationships are explicit and clinically reviewed, and neither requires the other. ALV-N022 builds only the **non-semantic** correction in section 2.1 of the briefing. |
| OD-10 (F-088) | Discarding an empty draft encounter | **PROPOSED** | An empty draft (no entries, chart changes, note text, vitals, diagnoses or completions) may be discarded by its author. The draft is kept with a Discarded mark and audited; it is never deleted. Needs the owner's yes or no. |
| OD-11 (Q-5k) | The author's own safety alert counts as acknowledged by them | **PROPOSED** | Owner's choice. |
| OD-12 (Q-5o, Q-5x) | Second template with confirmation; "all 0" per perio tooth | **PROPOSED: record as design limits** | Not built unless the owner asks. |
| OD-13 | Signed (locked) but not finalized drafts under OD-06 | **PROPOSED** | A signed draft is already locked against edits. The proposal: completion and diagnosis attachment are refused with 409 `encounter_signed` until it is unsigned. |
| OD-14 | Legacy encounter entries not on the chart (migration choice, briefing section 5) | **PROPOSED: option M-A** | No automatic copy. Legacy entries stay as they are and are shown as recorded in that encounter. Where no chart item matches, a clinician can add one with a single click (provenance kept). |
| OD-15 | Register housekeeping: O-G4a..O-G4p and O-5d..O-5g filed under PKG-06 | **PROPOSED** | Re-home or close as observations (owner or reviewer call). |

## 3. F-105 future-work note (recorded, not authorized)

A separate, provider-facing **Medication Reference and Provider Lookup** feature may later use public factual sources for normalized names, ingredients and label facts. It may also offer an interaction lookup that the provider explicitly starts. That feature is **not authorized now**. When it is proposed, it must not send patient identity, chart context, diagnoses, allergies or any other patient data to an external service.

## 4. What is not decided by this record

There is no implementation, migration, API integration or broad test run until the briefing (`docs/design/ALV-N022_PRE_IMPLEMENTATION_BRIEF.md`) is approved by independent review.
