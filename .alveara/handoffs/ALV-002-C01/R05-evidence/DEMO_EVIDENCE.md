# ALV-002-C01 R05 — Demo evidence

Visible behaviour: the "Someone else changed this while you were editing" banner on the calendar drawer, the patient workspace and the visit board. The real-backend walkthroughs trigger the conflict on each page and run axe in both themes; they pass with the page overrides removed, and fail (axe `color-contrast` on `.alv-concurrency-conflict__title`) when only the banner colour is reverted. The change is a text colour; the measurements are the evidence.
