# ALV-N022 - owner decision record (revision 2)

- **Story:** ALV-N022 (execution item 59): R05 PKG-06 (safety and clinical-record integrity) and PKG-07 (finalized encounters, addendum).
- **Date:** 2026-11-16. **Session:** CC-20261115-m7x2.
- **Revision 2** answers review `.alveara/reviews/ALV-N022/BRIEFING-R01.md` (BR-2, BR-3, BR-4). Revision 1 is in commit `0a19afc`.
- **Owner approval of revision 1:** Firas Baidhani, 11/16/2026, given on revision 1 as a whole. Per review BR-2, that signature is **not** read as approval of the agent's proposed details. It covers only what the owner wrote: the owner's words are quoted in column 2 below.
- **Owner approval of revision 2 (covers only the rows marked DECIDED):** [selected] Approved - _Firas Baidhani_ (date) _11/16/2026_

## 0. What kind of decisions these are

The DECIDED rows are the owner's **product-record integrity decisions**:
- which record is current;
- what is refused;
- what is preserved;
- what a finalized encounter accepts.

**No clinical sign-off has been given, and none is claimed.** No clinical meaning is decided here: PD-09d is open, and PD-12 is deferred.

Column 3 holds the acceptance criteria. The agent proposed them for review, and they are not owner wording.

## 1. Decided by the owner

| Id | Owner's decision (owner wording, 2026-11-16) | Acceptance criterion (proposed by the agent, for review) | Attempt |
|---|---|---|---|
| OD-01 (PD-09a) | "The patient chart becomes the single current source for allergies, medications, and medical history." | After any add, change or status change of an allergy, medication or medical-history item from an encounter, the Safety tab, the banner, the chart and the prescribing allergy check show the same items without reload. **Dental history is not included** (BR-3): its model and behaviour stay as they are. | A1 |
| OD-02 (PD-09a) | "Encounter actions update that chart and retain an immutable 'recorded/reviewed in this encounter' provenance entry." | Every chart change or section review made from an encounter appends a version or event carrying that encounter's id. A finalized encounter's provenance view never changes afterwards. | A1 |
| OD-03 (PD-09b) | "Exact normalized duplicate allergies are refused; the user is directed to the existing item for amendment. Never silently merge records." | Allergy items only (BR-4; corrected by Addendum 02, review A1-01). A new allergy's name is canonicalized (trimmed, inner whitespace collapsed, case kept); comparison ignores case. If a live allergy with that name exists, no item is ever added: (1) **Active** match - 409 `duplicate_item` with the existing id, the user is directed to update that item; (2) **Resolved/Inactive** match - 409 `duplicate_item` with the existing id and status, the user is offered an explicit **reactivate/update the existing record** action and must choose the new status (none pre-selected; nothing changes until submitted); (3) **Cancel** leaves every item unchanged; (4) **concurrent adds** leave one record (the existing unique index) and the losing request receives the same existing-record direction. A retried identical add replays. Historical rows are never rewritten or merged. Medication and medical-history chart duplicate behaviour is unchanged; in the encounter, a matching live item is offered to confirm current or update (Addendum 01 S-1 A). | A1 |
| OD-04 (PD-09c) | "'None reported' is unavailable when an active item exists in that section, and old contradictions are visibly flagged rather than erased." | The action is not offered, and the API refuses it with 409 `section_has_active_items`. A stored contradiction is shown as one, and both records are kept. | A1 |
| OD-05 | "resolve stale/duplicate safety-banner behavior" | One banner per page, refreshed after every successful clinical-record write | A1 |
| OD-06 (PD-10 A) | "A finalized encounter accepts no new clinical care, including procedure completion/adoption and diagnosis attachment. Later care belongs in a new draft encounter. Corrections to the finalized narrative remain append-only text addenda." **Amended 2026-11-17 (owner, on review A2-STEP0-ADDENDUM-01): finalized-encounter refusal applies to diagnosis creation and re-link only. Completion, adoption and append-only completion amendment may attribute to a same-patient Draft or Finalized encounter without changing it (ALV-008-C01 D6 stands).** | Diagnosis create (and any re-link) refuse a **Finalized** encounter with 409 `encounter_finalized` and write nothing; the diagnosis picker lists only non-finalized encounters. Completed-procedure completion, adoption and amendment are unchanged: same-patient Draft or Finalized attribution, the encounter never mutated. Documents and referral cases unchanged. The addendum route is unchanged. **Signed drafts keep today's behaviour** (OD-13 is not decided). | A2 |
| OD-07 | "retain F-106 prescription display improvements in scope" | The Prescriptions tab shows status, instructions and quantity, and marks a superseded version. | A3 |
| OD-08 (F-105 / PD-12) | "Defer, do not remove, the allergy-class feature ... do not add an allergy-class seed list; do not add drug-to-drug, contraindication, dose, compatibility, or prescribing-decision logic; do not integrate any public or commercial drug API in ALV-N022; retain the existing direct recorded-name allergy warning only; ensure the absence of a direct-name warning is never presented as a medication being safe, compatible, approved, or clear." | Every surface showing "no recorded match" says it is a name comparison only, not a clearance. A test fails on verdict wording. The plan (item 59), R05 (PKG-06 Group C, PD-12, row 6.07) and the register (F-105 note) record the deferral as a release limitation. | A3 (wording); plan and R05 amended in this revision |
| OD-09 (PD-09d) | "Do not infer clinical meaning for Diagnosed/Planned 'missing tooth,' extraction intent, or implant relationships. Keep PD-09d open and identify the smallest safe non-semantic correction separately." | The meaning stays **OPEN**. Only the display correction of briefing 2.1 is planned, and it is subject to step-0 verification. | A3 (display only) |

## 2. Proposed (not decided; not built; not in any attempt's test contract)

| Id | Item | Proposal |
|---|---|---|
| OD-10 (F-088) | Discard an empty draft encounter | An empty draft may be discarded by its author. It is kept with a Discarded mark, audited and never deleted. R05 lists this under Group B; it is not built until the owner decides. |
| OD-11 (Q-5k) | The author's own alert counts as acknowledged | The owner's choice |
| OD-12 (Q-5o, Q-5x) | Second template; "all 0" perio | Record as design limits |
| OD-13 | Signed but not finalized drafts under OD-06 | Refuse with 409 `encounter_signed` until unsigned. **Removed from A2** (BR-2). |
| OD-14 | Legacy encounter entries not on the chart | No automatic copy. An explicit "add to chart" confirmation flow (briefing 5, BR-5). The owner approved no backfill ("never silently merge"), and the reviewer retained it. The flow itself is proposed. |
| OD-15 | O-G4a..O-G4p and O-5d..O-5g filed under PKG-06 | Re-home or close as observations |

## 3. F-105 future work (recorded, not authorized)

A provider-facing **Medication Reference and Provider Lookup** feature may later use public factual sources for normalized names, ingredients and label facts, with a possible provider-initiated interaction lookup. It needs its own approved design. It may not send patient identity, chart context, diagnoses, allergies or any other patient data to an external service. Until it is delivered, the missing allergy-class check is a **release limitation**.

## 4. Not authorized by this record

No implementation, migration, external lookup, allergy-class logic, or focused, browser or regression run is authorized until the briefing is independently approved.
