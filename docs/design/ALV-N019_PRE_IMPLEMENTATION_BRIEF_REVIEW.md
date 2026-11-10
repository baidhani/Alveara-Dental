# ALV-N019 Pre-Implementation Brief — Independent Review

Date: 2026-11-09

Reviewed artifact: `docs/design/ALV-N019_PRE_IMPLEMENTATION_BRIEF.md`

Decision: **APPROVED FOR STAGE 1 IMPLEMENTATION**

## Evidence supporting approval

The briefing satisfies the binding pre-implementation disclosure rule. It states the intended result, lists planned changes and retained behavior, identifies affected data/permissions/audit/recovery concerns, maps source items to concrete acceptance assertions, provides focused Stage 1 and frozen Stage 2 plans, and reproduces the relevant raw-log material from the approved v6 package.

The DentalKeep inventory correctly separates public product surfaces from historical/control material and compatibility-sensitive identifiers. The proposed public copy changes are specific. The inventory recognizes that namespaces, schemas, persisted keys, format identifiers, control-package names, course artifacts, and historical evidence cannot be renamed by bulk replacement.

## Decisions made for this implementation

These decisions resolve the briefing's open items for the approved Stage 1 scope:

| Brief item | Decision |
|---|---|
| O-1 | Approve option 1: chain-only composition with the production `Program.cs` path unchanged by default. Any need to alter production service registration, production clock behavior, authentication expiry, or retry behavior requires a new pre-implementation review. |
| O-2 | Approve the scoped chain runner, stage-0 Playwright configuration, disposition register/check, tracked-exception register, and brand-inventory check. They must remain test infrastructure and follow the safety boundaries in the briefing. |
| O-3 | Use the owning `ALV-N0xx` package ID as the tracked-exception `owner`; use the home package’s approved acceptance evidence as its `exitCondition`. A missing owner or exit condition is a hard runner failure. |
| O-4 | Keep the course Command Center, `CLAUDE.md`, root README course wording, course artifacts, GitHub repository name, immutable handoffs/reviews, and `ALV-*` / `Alveara_Handoff_*` control identifiers unchanged. |
| O-5 | New exports, backups, and recovery-key downloads use the `dentalkeep-` prefix. Existing `alveara-` files and `.abk` archives must remain usable; tests must demonstrate that compatibility. |
| O-6 | New export `source.system` is `DentalKeep`. Import continues to accept both historical `Alveara Dental` values and the new value without changing import semantics. |
| O-7 | No package upgrade is approved in this attempt. Complete the read-only advisory report first. Any package update needs the separate PD-26 owner decision and a reviewed scope addition. |
| O-8 | Any accepted risk for a shipped dependency requires explicit product-owner approval. A dev-only advisory may be reviewed only after the report proves it is dev-only and unreachable from shipped artifacts. |

## Implementation constraints

1. Implement only the reviewed ALV-N019 scope. Do not begin a later stabilization package or release engineering.
2. Do not run broad regression before Stage 1 review. Run the focused Stage 1 checks in the approved briefing after the implementation exists, then package Stage 1 evidence for independent review.
3. Preserve compatibility-sensitive values exactly as classified. The brand check must pin individual retained identifiers and paths; it must not make unrelated future code maintenance fail merely because a file containing such an identifier changes.
4. The `F-001`/`F-005` advisory report must be complete before this story is submitted for final closure. The repository's dependency rule requires known CVEs to be resolved before another change ships. If a vulnerable shipped dependency remains, record the factual blocker and obtain the required PD-26 owner decision; do not silently classify it away.
5. Preserve old export/import and backup/restore fixtures in focused coverage. New public file names and `source.system` values must not make older artifacts unreadable.
6. The runner must never read, write, reset, or delete `AlveraDev`, `Dev-Data`, real backups, or any non-run-owned path/database. Its cleanup self-test must prove refusal and containment behavior.

## Next action

Implementation of ALV-N019 may begin under the current two-stage review policy. A successful coding attempt ends at `AWAITING_REVIEW` with a Stage 1 handoff. It must not run the frozen full regression until independently approved as `APPROVED_PENDING_REGRESSION`.
