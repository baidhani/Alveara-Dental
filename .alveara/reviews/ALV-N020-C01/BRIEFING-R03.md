# ALV-N020-C01 — Pre-Implementation Briefing R03 Review

- **Decision date:** 2026-11-12
- **Reviewed artifact:** `docs/design/ALV-N020-C01_PRE_IMPLEMENTATION_BRIEF.md` (revision 3; document-only commits `8537844` and `593b1f4`)
- **Decision:** **APPROVED FOR IMPLEMENTATION**
- **Regression route:** Focused evidence; broad regression remains `DEFERRED_TO_ALV-N028`.

## Approval basis

Revision 3 resolves every open correction from `BRIEFING-R02.md`:

1. A new deployment assigns `ManageLoginPresentation` explicitly to the bootstrap's first administrator, while an existing installation assigns no holder by inference and requires an explicit, audited operator grant.
2. Historical Manual E2E identifiers were removed rather than incompletely paraphrased; no Manual E2E raw entry drives this owner-approved feature.
3. The owner's standing five-minute permission policy is recorded in the execution plan and applied accurately. Long or unknown-duration work still needs the owner's explicit permission.
4. The bundled-image tile-readiness gate requires real-browser screenshots for every bundled image, with reprocessing or Tile refusal when an objectionable seam is found.

The owner-approved cleaned logo, teal-doodle deployment default, `Username` / `Sign in` wording, Tile/Stretch/Fill modes, controlled local catalog, metadata-free derivative pipeline, narrow anonymous endpoint, public-image warning, plain-text configurable notice, backup/restore proof, and account-scoped permission model are approved as briefed.

## Closed design choices

- **Q-3:** Approve the ordinary recoverable model: a `ManageUsers` holder may grant or revoke `ManageLoginPresentation` for an enabled named account. Every transfer must be CSRF-protected and audited. The feature holder does not become a hidden or irreversible super-admin.
- **Q-4:** Approve refusal of an attempt to inactivate the currently selected added image (`409 background_in_use`). The user must select a different image first. Keep the defensive read-time fallback as specified.
- **Q-5:** Approve a 30-active-added-image catalog cap, a 2,560-pixel derivative long-side cap, and a 10-line-break footer-notice cap. These are server-enforced limits and must be covered by the planned boundary tests.

## Implementation authorization and limits

Implementation may now begin exactly within the approved briefing and execution-plan scope. Do not expand into scheduled rotation (`ALV-N029`), general configuration busy-state work (F-075 / `ALV-N027`), a hidden/special password account, remote assets, other upload formats, or unrelated authentication changes.

Use the stated focused evidence plan. A focused test group reasonably expected from documented timing to finish within five minutes may run under the product owner's standing permission; an unknown or longer group, every qualifying chain/walkthrough, and all broad/full regression require the owner's explicit permission before start. Record permission route and expected/actual duration for every run. Do not run broad regression for this item.

When implementation is ready, prepare the immutable Stage 1 handoff with complete acceptance mapping and focused evidence, then stop at `AWAITING_REVIEW` for independent review. Do not mark the item complete.
