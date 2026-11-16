# ALV-N021 A2-R01: next-story impact

## ALV-N021 attempt A3 (decision 02)
T-1 and T-4 are authorized as A3 after an approved A3 pre-implementation briefing. Known A3 inputs from A2:
- The client's "today" is the browser's practice-zone day while the server's is the injected clock; walkthrough specs set the date explicitly and chain case 3.13 asserts Today = the browser's day.
- With a frozen injected clock several events share one instant and the history lists them by type; tests must not rely on history order.

## ALV-N022 (clinical safety and safety presentation)
- **Q-5q (owner, 2026-11-16):** decide whether the front desk receives a minimal non-clinical safety-presence indicator (for example "Clinical safety alert — notify the treating team") while allergy names, alert narratives, diagnoses and other clinical details stay withheld unless separately authorized. The permission matrix is unchanged in A2; the current denial test stays as proof of today's behaviour.

## ALV-N028 (program regression)
- The A2 rows in `.alveara/PROGRAM_REGRESSION_LEDGER.md`: full serial backend suite incl. every A2 class, the 35 walkthroughs outside A2, migrations M1-M6 on a populated database.

## Separate proposals (not built)
- OD-33..OD-35 (ALV-N030 replace appointment, ALV-N031 confirmation reminders, ALV-N032 late/automatic no-show and fee), placed by OD-36.
- F-071 part 2: freeing unused time after an early finish would be an operational-capacity proposal (actual-end-time model, conflict rules, rendering of a completed event over a later booking, audit, clinical/operational approval).
- Older pages' plain links (for example Referrals & labs) probably have the same dark-mode contrast problem fixed in A2 for the Schedule page and the calendar drawer; not changed in A2.
- The checkout panel offers every outcome the server lists for an item and explains a 403 for a role the recorder refuses; filtering the outcomes per signed-in user on the server would be cleaner.
- A retried blocked-time add can still create a duplicate block (pre-existing since ALV-N003, unchanged).

## Data and operations
- Migrations M1-M6 are additive. M1 to M5 each refuse to roll back once their new data exists (M1 51280, M2 51281, M3 51284, M4 51285, M5 51286), so recorded data is never discarded to make a rollback succeed; M6 rolls back freely (it holds no data).
- A development database needs M1-M6 applied and an administrator to set the two visit-workflow settings if the practice wants them off.
