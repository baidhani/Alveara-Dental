# ALV-N001 R04 — Demo evidence

Visible behaviour: the four notification variants (`/showcase` -> Info / Success / Warning / Danger) and the shell session-warning banner. The real-browser test `e2e/notification-contrast.spec.ts` renders all four variants in both themes and runs axe `color-contrast` on them (old CSS: fails at 4.45:1 and 3.46:1; new CSS: passes). The 8 real-backend suites exercise the dark-theme `--color-info` links and buttons on the real pages with axe scans. No screenshot is needed: the change is a text colour and the measurements are the evidence.
