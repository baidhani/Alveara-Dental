# ALV-N003 R03 — Demo Evidence

The R01/R02 end-to-end demonstration (real Chromium, real API/database: practice → location → appointment type → operatory → staff → provider → weekly hours → scheduling preview → inactivation → audit viewer, plus the real-dialog navigation guard) is unchanged and passes 11/11 against the R03 code.

## Behaviour demonstrated by component tests this attempt

- **Returning to the same provider is a new editing session.** After saving A, switching to B and back to A, typing a new draft, and then having the *old* save response arrive: the draft stays on screen, "Unsaved changes" remains, there is no "Weekly availability saved." notice and no conflict banner, and saving afterwards submits the new draft at the revision the new session loaded.
- **Typing while a save is in flight is safe.** The Save button reads "Saving…" and is disabled; text typed afterwards survives the response; the saved baseline and revision advance, so the typed change is still flagged unsaved and saves at the new revision.
- **Blocked time:** a late add (success or error) or remove from an abandoned session never wipes the new form, never shows an error and never announces success in the new session.
- **Generic panel and practice form:** while a save is in flight the form fields, the other rows' Edit buttons and Add are disabled, so a response can never land on a different record; a conflict reload that finishes after the user closed the form does nothing.

## Full regression

Backend 262/262; frontend 122/122 (`tsc`/build/lint clean); real-browser 11/11. See `artifacts/playwright-real-backend/run-output.txt`.
