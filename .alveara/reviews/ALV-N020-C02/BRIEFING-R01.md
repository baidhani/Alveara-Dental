# ALV-N020-C02 — Pre-Implementation Briefing R01 Review

- **Decision date:** 2026-11-12
- **Reviewed artifact:** `docs/design/ALV-N020-C02_PRE_IMPLEMENTATION_BRIEF.md`
- **Decision:** **APPROVED FOR IMPLEMENTATION**
- **Regression route:** Focused evidence only; broad regression remains `DEFERRED_TO_ALV-N028`.

## Approval basis

The briefing faithfully reproduces the owner observation, preserves ALV-N020-C01 as a closed record, and confines C02 to login layout polish plus the separately approved static SVG substitution. It correctly inventories `dentalkeep-logo-v4.svg` (SHA-256 `9CECF4A6C72A91FC7C8CF225805CA7DFD756F61760EE6FE2434F1C2E76ED82C0`), uses it as a locally bundled image, preserves the accessible name `DentalKeep`, and corrects the former raster slot’s incompatible aspect ratio.

The SVG's supplied composition—coral `dental` above larger teal `KEEP`—is accepted as the intended owner-provided brand treatment. Do not alter that artwork. If the owner later says the vertical ordering was unintentional, stop and obtain a replacement asset or a new approved decision.

The approved implementation scope is:

1. Login-form-only equal widths for Username, Password, and Sign in.
2. Rebalanced desktop logo/form spacing that contracts safely in narrow and 200%-equivalent layouts.
3. A readable 12 px footer notice with the existing colour system and plain-text behavior unchanged.
4. The named reviewed SVG replacing only the C01 raster logo.

## Implementation authorization and limits

Implementation may begin within the briefing and plan scope. Do not modify shared `FormField`, wording, notice content/configuration, backgrounds other than the named logo substitution, routes/endpoints, permissions, server behavior, upload/backup handling, schema/migrations, dependencies, authentication/session security, F-075/ALV-N027 work, or ALV-N029 rotation.

Use focused evidence. The owner’s standing permission covers a focused group reasonably expected to finish in five minutes or less; unknown/longer groups and all broad/full regression need explicit owner permission. Record each route and expected/actual duration. Do not run broad regression for C02.

Before packaging R01, provide the approved focused proof: all specified desktop/narrow/200%-equivalent/forced-colours layout checks, SVG integrity/ratio/accessibility assertions, before/after screenshots across the five bundled backgrounds, the existing affected login-browser checks, and effective focused negative controls. Then package the immutable Stage 1 handoff and stop at `AWAITING_REVIEW`.
