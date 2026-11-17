# ALV-N022 - owner decision request: PD-09, PD-10, PD-12 (draft for the owner; 2026-11-16)

R05 requires these decisions **before** dependent behaviour is built (execution plan item 59). Each has options, what each would change, and a recommendation. Survey: `docs/design/ALV-N022_FINDING_SURVEY.md`.

## PD-09 - clinical meaning (clinical sign-off)

### PD-09a: one source for allergies, medications and medical history (F-078, F-080, F-084, F-085, F-086, F-111, F-112, Q-5f/g/i)
Today an encounter keeps its own entries, separate from the chart the banner reads.

| Option | What it means | Impact |
|---|---|---|
| **A (recommended)** | The chart is the single source. "Add allergy/medication/history" inside an encounter adds to (or updates) the chart item, recorded as done in that encounter; the encounter shows the chart's items and what it changed. The banner and Safety tab then always agree. | Medium-large: encounter write path and reader change; existing encounter entries migrate to chart items (or are shown as historical); no loss of history. Migration likely. |
| B | Keep both; on save, copy encounter entries into the chart. | Two copies to keep consistent; duplicates and contradictions remain possible. |
| C | Keep both; only warn when they differ. | Smallest; leaves the S1/S2 findings partly open. |

### PD-09b: a duplicate allergy (F-084)
**Recommended:** refuse adding an allergy that is already active on the chart (same name, case-insensitive); offer "update the existing one" instead. Alternatives: warn and allow; merge automatically.

### PD-09c: "Reviewed - none reported" with an active allergy (F-080, F-112)
**Recommended:** not offered and refused while the chart has an active item in that section; an existing contradictory mark is shown as a contradiction. Alternative: allowed but shown with a warning.

### PD-09d: missing tooth, Diagnosed/Planned (F-091) and implants (Q-5v, Q-5w)
Today a missing tooth recorded as Existing/Completed refuses other findings; Diagnosed/Planned does not.

| Option | Meaning |
|---|---|
| **A (recommended)** | Diagnosed/Planned "missing" means the tooth is expected to be removed: other findings may coexist, and the chart shows a visible warning "planned for extraction" on the tooth. An implant on a tooth with an Existing missing finding is allowed and shown as "replaced by implant"; an implant does not require a recorded missing tooth. |
| B | Diagnosed/Planned missing refuses other findings too (same as Existing). |
| C | Clinical lead defines a different rule. |

## PD-10 - finalized encounters and the addendum route (owner with clinical lead) (F-103, Q-5ad)

Today completion (and diagnosis attachment) accept a finalized encounter. A text-only addendum already exists for finalized encounters.

| Option | What it means |
|---|---|
| **A (recommended)** | A finalized encounter accepts **no new care**: completion, adoption and diagnosis attachment require a Draft encounter (the pickers list only drafts; the API refuses a finalized one with a clear code). Care done later is recorded in a new encounter. A correction to the finalized note is the existing text addendum (original kept, author and time recorded). No new permission. |
| B | Add an "amendment encounter" linked to the finalized one, holding the late care, with its own sign-off and a new permission (for example `AmendFinalizedEncounter`). |
| C | Allow care on a finalized encounter only through an explicit addendum with a reason. |

## PD-12 - the allergy-class data (owner) (F-105, F-106)

Today the allergy check is a name comparison. An allergy-class check needs a table "medication -> allergy classes" (for example amoxicillin -> penicillins).

| Option | What it means |
|---|---|
| **A (recommended)** | A small, clinically reviewed seed list shipped with the product (penicillins, cephalosporins, sulfonamides, NSAIDs, opioids, local anaesthetics of the amide/ester groups - the classes relevant to dentistry), versioned, read-only in the product; changes only through a reviewed release. The check shows "not checkable" for anything not in the list. No data leaves the product. |
| B | As A, plus administrators may add entries in the product (permission, audit). |
| C | No class check; keep the name comparison and say so clearly. |

The clinical content of the seed list (option A or B) needs a named clinical reviewer.

## What happens next

On the owner's answers (and the clinical sign-off for PD-09 and PD-10), the agent writes the decision record and the pre-implementation briefing for independent review. No code before the briefing is approved.
