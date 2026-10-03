# ALV-N004 R06 — Demo evidence

Visible behaviour: the backup page's warning alerts, the restore wizard's warning and blocking check lines, and the settings form keeping an admin's edit when the permission set resolves after the first load. `e2e/backup-warning-contrast.spec.ts` renders the page and the wizard's check list in a real browser in both themes and runs axe `color-contrast` (old CSS: fails at 3.51:1; new CSS: passes). The edit-preservation behaviour is pinned by the deterministic unit test. The change is a text colour plus a loading-logic fix; the measurements are the evidence.
