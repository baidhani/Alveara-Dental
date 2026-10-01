# ALV-002-C01 R04 - Acceptance Evidence

| Finding | Evidence |
|---|---|
| GATE-A-02: scrollable audit region not keyboard-focusable (axe `scrollable-region-focusable`, both themes) | `role=region` + accessible name + `tabIndex=0`; `AuditLogPage.keyboard.test.tsx` (named region in tab order, Tab lands on it, jest-axe clean); browser evidence: reachable by Tab, arrow keys scroll it, rule no longer fires, both themes |
| Clearly identified region with visible focus behaviour | accessible name "Audit log entries (scrollable table)"; `:focus-visible` 2px outline from `--color-focus-ring` (computed style asserted in the browser, token asserted in the unit test) |
| Demonstrate keyboard navigation/scrolling with overflowing representative data | `gate-a-audit-keyboard.spec.ts`: real audit rows, table 1105px in a 528px container, `scrollLeft` 0 -> 240 after ArrowRight |
| Preserve audit permission checks, filtering/content and immutability | unchanged code paths; Dentist still denied page and API (`403`); original AuditLogPage tests and AuditLogImmutability backend tests unchanged and passing |
| Rerun authenticated axe in both themes | done (0 violations, both themes, focused state); the Gate A rerun will repeat the full scan |

Original six acceptance items: unchanged and re-asserted.
