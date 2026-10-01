# ALV-N001 R03 - Demo Evidence

Hover or keyboard-focus any secondary button in the dark theme (for example "View"/"Disable"-style actions on the security administration pages, or the Component Showcase): the text and border now switch to the readable accent colour instead of a barely visible dark blue. The danger button's label in the dark theme is brighter. Light theme is visually unchanged. Evidence: `artifacts/n001-axe-state-scan.json` (0 violations across default/hover/focus in both themes) and `buttonContrast.test.ts`.
