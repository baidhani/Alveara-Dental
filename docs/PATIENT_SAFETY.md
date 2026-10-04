# Patient safety: alerts, medical risk context and clearance tracking (ALV-N011)

ALV-N011 gives every clinical screen one trustworthy answer to "what should I know about this patient before I diagnose, plan, treat or prescribe?" - and is just as careful about
what it does **not** know. It is a patient-safety *context*, not a decision engine: it does not check drug interactions, doses or contraindications, and it never infers a risk from
unrelated data (those need validated future sources). It builds on the clinical record of `ALV-005-C01`.

## What is shown, and where it comes from

| Entry | Origin | How it gets there |
|---|---|---|
| **Allergies** (active) | the patient's **clinical record** | read live - never copied. Recorded severity maps to the safety scale (Severe -> High, Moderate, Mild -> Low); an allergy with no recorded severity says "Severity not recorded" and is flagged for review, it is never guessed. |
| **Current medications** (active) | the clinical record | read live, with dose and frequency; a medication has no severity. |
| **Alerts** - significant condition, pregnancy, anticoagulant status, adverse reaction, custom | a clinician **stated** them | created explicitly with a title, a severity (Critical / High / Moderate / Low), optional details and a **required source** ("reported by the patient at intake", "cardiology letter"). |
| **Clearances** - medical or dental | the practice's own workflow | requested -> received -> resolved (or cancelled). |

Nothing is created because a field is empty. A patient with nothing recorded has **no entries** - and the context says what is *not established* instead:

- **Gaps** (`gaps`): the clinical-record sections that matter here (allergies, medications, medical history) that were never reviewed, are unknown, or changed since they were
  confirmed - each with the sentence "Nothing listed here does not mean none." A section a clinician reviewed (or marked none known) produces no gap.
- The statements `none known` / `unknown` / `not reviewed` stay distinct (they come from the clinical record, `ALV-005-C01`) and are never turned into alerts.

## Acknowledged is not resolved

Acknowledging an alert records, in its own **append-only** table, that *this person saw this revision* - nothing more. It never changes the alert's status:

- The alert stays **Active**; the screen says "You acknowledged this ... It is still active - acknowledging does not resolve it."
- An acknowledgement belongs to one person and one **revision**. Any change to the alert (edit, resolve, reopen) moves the revision, so earlier acknowledgements stop counting and
  it needs to be seen again. Acknowledging an old revision is refused (`alert_changed`) so nobody acknowledges what changed under them.
- Only an explicit **resolve** closes an alert, and it needs a **reason**; it records who and when. Reopening needs a reason too. Every step appends a full version (what it said,
  who, when, why). An alert is **never deleted**.

## Clearances

`Requested -> Received -> Resolved` (or `Cancelled`), each step with who, when and why:

- **Received is not resolved.** A received clearance stays open ("Received - not yet resolved") until someone resolves it with a reason.
- A clearance **still waiting cannot be resolved** (`clearance_not_received`); resolving and cancelling need a reason; a closed clearance cannot be changed.
- The **supporting document may not be available yet**: a clearance can be received - even resolved - without one; it then says "Supporting document not yet attached" and the
  reference can be attached later (`.../document`). The reference is free text until a documents story exists; it never pretends to be a stored file.
- Requesting the same open clearance again (same kind and reason) is quiet, enforced by a unique index on a hash of the reason.

## The shared context (for downstream screens)

`ISafetyContextProvider` (implemented by `SafetyContextService`) is the query the odontogram, perio chart, diagnosis, treatment planning, procedure completion and prescribing screens
use, so they all read the same picture:

- `GetAsync(patientId, forUserId)` -> `SafetyContext`: `entries` (active, most urgent first), `resolved` alerts (kept with who resolved them and why), `clearances` (open first),
  `gaps`, and a `summary`. Reading is a **read-only projection**: it acknowledges nothing, resolves nothing and writes nothing.
- `SummaryAsync` -> counts and the highest severity only (no names) - what the header shows.
- Each alert entry carries its `revision`, `rowVersion`, `acknowledgedByMe`, and `needsAttention` / `attentionReason`: an alert whose clinical-record source item was later resolved
  or removed is **flagged for review in words** - stale or conflicting data is shown, never silently corrected or hidden.

## The live visit board: a minimal indicator

The shared visit board (`GET api/visits/board`) may carry, per visit, `safety: { alert, clearance }` - **two booleans and nothing else** (no category, name, severity or count):

- `alert` = the patient has an active alert **or** an active allergy recorded as severe; `clearance` = a clearance is requested or received.
- Only callers who hold `ViewSafetyIndicator` receive it (Dentist, Hygienist, Assistant, Admin). Front desk, the practice manager and billing do not (billing cannot read the board at
  all); for them the field is absent. A patient with nothing to show has no indicator - there is no "all clear".
- The card says "Safety alert on file" and/or "Clearance open" in words. Details are only inside the authorized patient context (the Safety tab).

## Permissions

| Permission | Who | Used for |
|---|---|---|
| `ViewClinicalDocumentation` (existing) | Dentist, Hygienist, Assistant, Admin | reading the context, summary and histories; **acknowledging** an alert (it only records that they saw it) |
| `ManageClinicalNotes` (existing) | Dentist, Hygienist, Admin | creating, changing, resolving and reopening alerts; every clearance step |
| `ViewSafetyIndicator` (new, appended last) | Dentist, Hygienist, Assistant, Admin | the minimal indicator on the live visit board |

## API

| Endpoint | Purpose |
|---|---|
| `GET api/patients/{id}/safety`, `.../safety/summary` | the context; counts only |
| `GET api/safety/alerts/{id}/history`, `GET api/safety/clearances/{id}/history` | every version |
| `POST api/patients/{id}/safety/alerts`, `PUT api/safety/alerts/{id}` | state / change an alert (a source is required: `source_required`) |
| `POST api/safety/alerts/{id}/resolve`, `.../reopen` | a reason is required (`reason_required`) |
| `POST api/safety/alerts/{id}/acknowledge` `{ revision }` | records that the caller saw that revision; never resolves |
| `POST api/patients/{id}/safety/clearances`, `.../receive`, `.../document`, `.../resolve`, `.../cancel` | the clearance workflow |

Every change needs a CSRF token and the row version read; a stale edit is the shared 409 `concurrency_conflict`. A refusal returns a stable `error`, a message safe to show and
`fieldErrors`. Audit text is PHI-free and deliberately generic ("Safety alert resolved.", "Medical clearance received (document not yet attached).") - no title, detail, source,
reason, document reference or even the alert category, because the audit log's readers are not cleared to see them.

## What the database enforces (migration `AddPatientSafety`)

Case-sensitive checks (alert category/severity/status, clearance kind/status, change types); a resolved alert needs who, when and a non-blank reason and an active alert carries none;
a clearance cannot be resolved without being received, and a received/resolved/cancelled one needs its stamps; one **active** alert per category and title per patient and one **open**
clearance per kind and reason; and triggers (51050-51054) that refuse deleting alerts and clearances, changing either history, and changing or deleting an acknowledgement.

## The screens

- **Safety strip** - in the patient header on every patient screen and at the top of an open encounter (before anything is documented): counts and the highest severity, alerts you have
  not acknowledged, items needing review, and every gap - all in words, with a link to the details. A failed read says "could not be loaded, so do not assume there is none".
- **Safety tab** - "Not established" gaps; active alerts and safety information (severity, kind and status as words; source; last updated by whom); allergies and medications read-only
  with a link to change them in the clinical record; add / change / acknowledge / resolve / reopen with reasons; the clearance workflow with its missing-document state; resolved alerts;
  per-item history. Typed text survives a refused save, a dropped connection and a stale edit (reloads refresh in place).
- **Visit board** - "Safety alert on file" and "Clearance open" for authorized roles.

## Tests

`SafetySchemaTests` (database rules), `SafetyAlertServiceTests`, `ClearanceServiceTests` and `SafetyContextServiceTests` (lifecycle, history, acknowledged-vs-resolved, reasons and sources,
no invented alerts, gaps, stale sources, audit, concurrency, the minimal indicator), `SafetyApiTests` (roles, CSRF, versions, stable errors, **board authorization and minimization**) and
`ClinicalPermissionTests` (extended); `Safety.test.tsx`, `FlowBoardSafety.test.tsx` and `safetyContrast.test.ts` (screens, board, contrast); and the real-browser walkthrough
`e2e/safety-real-backend.spec.ts` (see `docs/testing/REAL_BACKEND_E2E.md`).

## Known limits (not built)

- No drug-interaction, dose or contraindication checking and no external medical-information integration; the context never infers a risk.
- Allergies, medications and history are changed in the clinical record, not here; an alert can cite a record item through the API (`sourceItemId`) but the screen does not offer the link.
- The clearance document is a free-text reference until a documents story exists.
- Any `ManageClinicalNotes` holder can resolve or reopen an alert and run clearances; there is no separate sign-off role. Every step is audited.
- The board flag does not say which of an alert or a severe allergy raised it (by design); a severe allergy raises the alert flag.
- Reading the safety context is not itself audited (only changes and acknowledgements are).
- Clinical screens, including the new ones, were checked at desktop width only (no tablet-width run).
