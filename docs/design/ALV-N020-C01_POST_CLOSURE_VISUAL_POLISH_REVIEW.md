# ALV-N020-C01 Post-Closure Visual Polish Review

- **Decision date:** 2026-11-12
- **Reviewed request:** `docs/design/ALV-N020-C01_POST_CLOSURE_VISUAL_POLISH_REQUEST.md`
- **Decision:** **APPROVED AS A SEPARATE COMPANION STORY**

## Execution decision

`ALV-N020-C01` remains closed at approved attempt R02 and must not be reopened as R03. The three requested presentational refinements are authorized as the new, separately controlled `ALV-N020-C02 — DentalKeep Login Layout Visual Polish`, inserted after C01 and before ALV-N021 in the execution plan.

The coding agent must first make the deliberate execution-control record for C02 and create `docs/design/ALV-N020-C02_PRE_IMPLEMENTATION_BRIEF.md`. Do not modify CSS, tests, assets, application code, configuration, schema, dependencies, or run tests until that briefing has independent approval.

## Approved design choices

1. **P-1 — one equal-width column:** Fill the Username and Password inputs to the existing login-form column width, matching the Sign in button. Scope the selector only under the login form; do not modify shared `FormField` behavior.
2. **P-2 — balanced spacing:** Increase the normal desktop logo top and bottom spacing using the unused vertical panel space above the footer. At narrow and 200%-equivalent viewports, contract the spacing before allowing any clipping, overlap, horizontal scrolling, or unreachable footer/form content.
3. **P-3 — 12 px footer notice:** Use 12 px, not 11 px. The notice remains clearly secondary to form labels while maintaining a more defensible readability floor for an access/privacy notice. Retain a readable line height and the existing colour tokens.

## Scope guardrails

This is CSS/layout, the owner-supplied static SVG logo substitution, and focused browser-test evidence only. Replace only C01's raster logo with `C:\Users\firas\Desktop\Epic\dentalkeep-logo-v4.svg` (SHA-256 `9CECF4A6C72A91FC7C8CF225805CA7DFD756F61760EE6FE2434F1C2E76ED82C0`); bundle it locally and preserve its `1774×820` viewBox ratio rather than retaining the previous raster slot's dimensions. Its source was reviewed as static: internal paths/definitions only, with no script, event handler, external URL/reference, embedded font, image, or active content. No other logo/background asset change is authorized. Do not change wording, notice contents or configuration, routes/endpoints, permissions, server behavior, upload/backup handling, schema/migrations, dependencies, authentication/session security, or shared form components. F-075 remains with ALV-N027 and scheduled rotation remains ALV-N029.

Focused real-browser evidence, screenshots of all five bundled backgrounds before/after, SVG ratio/accessibility assertions, and focused negative controls are required. The standing five-minute permission policy applies; broad regression remains deferred to ALV-N028.
