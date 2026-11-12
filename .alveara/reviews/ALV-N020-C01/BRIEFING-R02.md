# ALV-N020-C01 — Pre-Implementation Briefing R02 Review

- **Decision date:** 2026-11-12
- **Reviewed artifact:** `docs/design/ALV-N020-C01_PRE_IMPLEMENTATION_BRIEF.md` (revision 2; document-only commit `d92f6ef`, later logo approval `19135f1`)
- **Decision:** **CHANGES_REQUIRED**
- **Implementation authorization:** **Not authorized.** This is a briefing correction only; do not change application code, schema, runtime configuration, assets, dependencies, or run tests.

## What is accepted

The revised briefing correctly recognizes this as a privileged upload plus anonymous-serving surface, rather than a cosmetic CSS change. Its local-only catalog, PNG/JPEG content detection, bounded dimensions and pixels, metadata-stripping derivative, server-controlled naming, narrow anonymous endpoint, selected-image-only serving, fallback, backup/restore proof, plain-text notice, and focused evidence plan are sound directions. The reviewed `dentalKeep logo v2.png` is visually clean and suitable for the approved full-height light-panel design.

Tile, Stretch, and Fill are approved as specified; Stretch must retain its visible distortion warning. The supplied teal-doodle image remains the deployment default. `Username` and `Sign in` remain the approved accessible wording.

## Required corrections

### 1. Never infer the owner on an existing installation

For a **new** deployment, the first administrator account created by the installation bootstrap may receive `ManageLoginPresentation` in that same transaction. The deployment flow must make that assignment explicit to the installer and audit it.

For an **existing** installation, remove the automatic "earliest-created enabled Admin" selection. It is an unsafe guess and may grant the presentation authority to the wrong person. Leave the permission unassigned until a deployment operator explicitly selects a named enabled account through the ordinary audited account-authorization path. Do not infer identity from creation time, display name, username, or role.

This remains an ordinary named account—not a hidden, preset-secret, recovery-bypass, or super-admin account. `ManageUsers` holders may use the documented audited grant/revoke path after deployment, consistent with the approved ordinary authorization model.

### 2. Preserve the complete raw source entries or remove the identifiers

Section 0 identifies F-156, F-012, F-013 and PD-01 as related entries but does not reproduce their full raw-source records. The execution-plan briefing rule requires the complete corresponding raw-log entry whenever an identifier is named as relevant. Either:

- reproduce the exact full raw entries, including source heading and observed-result detail, from the approved Manual E2E source; or
- remove an identifier that is only historical context and is not a scope/evidence driver for this item.

Do not retain abbreviated descriptions in place of a raw record.

### 3. Remove the purported five-minute standing permission rule

The line in section 8 saying runs occur under an owner "5-minute rule" is not authorized by the execution controls. Every executable test group still requires the product owner's explicit permission before it starts, regardless of estimated duration, unless the owner later adopts a separate written standing rule. The revised briefing must say this plainly. It must not run the listed chain or walkthrough evidence before that permission.

### 4. Close tile-asset readiness before packaging assets

T3 and T5 currently retain a visual-seam question in the asset inventory. A background offered in Tile mode must have explicit tiled browser screenshot evidence at representative viewport sizes, not a deferred "check visually" note. Reprocess/crop an asset, or prevent Tile mode for that asset, if the visual check shows an objectionable seam. Record the result for all five bundled images before implementation evidence is packaged.

## Resubmission required

Revise the briefing and return it for independent review. The resubmission may be document-only and must contain no implementation or test-run evidence. On approval, implementation may begin under the focused-evidence/deferred-regression route; broad regression remains owned by `ALV-N028`.
