# ALV-N002 R09 — Demo evidence

Visible behaviour: the "Status is stale." banner on `/system-status`, shown when a poll fails after a successful one. `e2e/system-status-stale-contrast.spec.ts` produces it in a real browser by driving the 15 s poll with the page clock, in both themes, and runs axe `color-contrast` on it (old CSS: fails at 3.46:1; new CSS: passes). The change is a text colour; the measurements are the evidence.
