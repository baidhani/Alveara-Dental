# Unassigned findings

A running list of findings that belong to **no story yet**: things the owner notices in a manual walkthrough, or that come up while a story is being built but are outside its approved scope. They are written down here instead of being slipped into whatever story is open. Each one is assigned later, by the owner, to a story (or closed).

This list is separate from `DISPOSITION_REGISTER.json`, which maps the raw ids of the first Manual E2E findings log and is checked for completeness; ids here (`U-nnn`) never reuse those.

## How to use it
- **Add:** next free `U-nnn`, the date, who found it and where, what was seen, why it matters, and a suggestion if there is one. Status `UNASSIGNED`.
- **Assign:** when the owner places it in a story, set status `ASSIGNED` and name the story (for example `ASSIGNED -> ALV-N024`). The story's own briefing then carries it.
- **Close:** `DONE` with the story and commit that fixed it, or `CLOSED` with the reason (not a defect, duplicate, won't fix).
- Nothing here is fixed outside a story.

## Findings

| Id | Date | Found by / where | What | Why it matters | Suggestion | Status |
|---|---|---|---|---|---|---|
| U-001 | 2026-11-17 | Owner, manual walkthrough after ALV-N022 (Periodontal -> Charts on record) | The field that picks the link type is labelled "Link to"; it does not say what is being linked. | A user reading "Link to" has to guess that the chart is the thing being linked; the form's accessible name is already "Link this chart", so sighted and screen-reader users read different words. | Label it "Link this chart to" (options unchanged). Update the tests that find the field by "Link to" (vitest: `PerioCompare`, `A3Display`; browser: `perio-sessions`, `a3-display`). | UNASSIGNED |
| U-002 | 2026-11-17 | Agent, ALV-N022 A3 screenshots (Odontogram; also older odontogram-longitudinal captures) | The save/status line on clinical screens is sticky at the top of the window (`.alv-clinical__status`, `position: sticky; top: 0`, since ALV-005-C01). On the odontogram, once the page is scrolled, it sits over part of the tooth chart. | It hides tooth tiles (and their state words) at exactly the moment the user is working on the chart. | Keep it visible but stop it covering content, e.g. reserve space for it or place it above the chart area instead of floating over it. | UNASSIGNED |
| U-003 | 2026-11-17 | Agent, ALV-N022 A3 focused vitest (`ClinicalNotes.test.tsx`, "shows the conflict banner for a stale tab...") | Under load the note box accepted typing before its saved text had loaded; the saved text then appeared in front of what was typed ("OriginalMy version"). The test also waits for the page heading rather than the note text, so it fails intermittently. | Possible lost or mixed-up note text if a clinician starts typing very quickly on a slow connection; and a flaky test. Also recorded as test debt in `.alveara/PROGRAM_REGRESSION_LEDGER.md`. | Decide whether the box should stay read-only until its text has loaded; make the test wait for the loaded text. | UNASSIGNED |
