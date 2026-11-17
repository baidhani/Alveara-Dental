# ALV-N022 A3 — Step 0 / Addendum 01 Review

**Document reviewed:** `docs/design/ALV-N022_A3_STEP0_AND_ADDENDUM_01.md`  
**Decision:** **CHANGES_REQUIRED — decisions and evidence-contract corrections required before implementation**

## Confirmed survey results

The read-only survey usefully confirms the main display defects and keeps the critical clinical boundaries intact:

- F-106 can use existing workflow-record fields; no new prescription field is needed.
- F-105 remains a wording/guard task only.  It must not add allergy-class, interaction, dose, compatibility, clearance, or external-API behavior.
- F-067, F-082, F-093, F-094, F-095, F-097, F-099, F-137, F-199, Q-5ai, and Q-5j are bounded display/usability changes.
- Q-5aj belongs to completed-procedure behavior and cannot become a chart-effect rule in A3.
- F-079 and F-089 are not reproduced by the identified current paths.

## CR-1 — F-091’s required display correction was removed without authority

The governing A3 briefing and Execution Plan require the bounded, non-semantic correction: each finding shows its recorded status and the **absent glyph is drawn only for an Existing or Completed missing finding**.  The survey instead proposes to drop the glyph portion because no glyph is currently drawn for any status.

That is not a finding that the requirement is already satisfied; it is evidence that the required bounded display correction remains to be implemented.  A3 must implement and test this exact presentation rule without inferring extraction intent, implant relationships, or any other missing-tooth clinical meaning.

## CR-2 — real-browser workflow omitted

Execution Plan item 59 requires real-browser clinical-workflow coverage and two-stage review.  The A3 plan names Stage 1/Stage 2, but contains no browser workflow.

Add one fresh-database, real-backend Chromium workflow to the focused Stage 1 contract.  It should cover a representative cross-section of the visible A3 behavior, including:

1. the prescription tab’s workflow-backed status/instructions/quantity/supersession presentation;
2. the odontogram’s status display and absent-glyph boundary (Existing/Completed only); and
3. one same-patient linked-record picker plus its refusal of another patient’s record.

State the expected duration.  If it would exceed five minutes, request owner permission before running it.  Stage 2 remains reviewer-defined after Stage 1 approval; broad/full regression stays deferred to ALV-N028.

## Owner decisions required

### D-1 — F-090 and F-098 linked-record storage

Choose one approach before implementation.

- **Recommended: Option B, typed nullable link columns with foreign keys.** Add a nullable patient-document id for clearance records and a nullable diagnosis id for periodontal links, preserve legacy free-text values as legacy display-only references, validate same-patient scope at service level and enforce it at the database layer where the existing schema permits.  Include migration up/down and same-patient/direct-persistence proof.  This is the durable record-integrity design.
- Option A retains the string columns and stores identifiers in them with service-only validation.  It is smaller, but permits invalid relationships through direct persistence and leaves the schema semantically ambiguous.

Q-5aa may be implemented only after this choice, using the typed periodontal-diagnosis relationship when Option B is selected.

### D-2 — F-092 source text

The raw complaint is unavailable and the visible current hint reads correctly.  Recommended decision: record F-092 as **NOT_REPRODUCED**, with the survey evidence; reopen it only if the owner supplies the missing raw-log line or a reproducible screen.

### D-3 — Q-5aj completion-result explanation

Recommended decision: add only the neutral result explanation, “No chart finding was linked to this procedure, so the chart was not changed,” when applicable.  It must not create a finding, alter the completed-procedure rule, or imply a clinical conclusion.  Alternatively defer it wholly to the next completed-procedure story.

## Required Addendum 02

Return a documents-only Addendum 02 that records the owner’s D-1/D-2/D-3 decisions, restores the F-091 glyph rule, adds the browser workflow and Stage 1/Stage 2 evidence route, and updates the A3 plan/test matrix accordingly.  Do not write production code, run tests, use an external medication service, add clinical decision logic, or begin A3 before that addendum is independently approved.
