# ALV-N026 PDF library proposal (R05): delta to R04 for the brief review

**Date:** 2026-11-21. **Status:** AWAITING_REVIEW (design only). **Base:** `ALV-N026_PDF_LIBRARY_PROPOSAL-R04.md`. Everything in R04 stands except the changes below.

**Answers:** `ALV-N026_PRE_IMPLEMENTATION_BRIEF_REVIEW.md` (CHANGES_REQUIRED). That review accepted the engine, fonts, format, runtime strategy, readiness, failure behaviour, release-store controls, server-side flow, render-folder controls and the D1 to D4 rules. The corrections concern the brief, plus the hash wording that R04 shares with it.

## Changes from R04
| R04 text | R05 |
|---|---|
| Section 1, verification: "canonical source-data checks (with a canonical-content hash where suitable)" | Canonical source-data checks assert historical values directly. **A canonical-content hash is an optional diagnostic only, never an acceptance check** (review item 5) |
| Section 2: "optionally, a canonical-content hash" on the issue row | Unchanged in substance, and now stated explicitly: diagnostic only. Statements and receipts keep their existing hash comparison as source verification of the ledger rebuild |
| Section 7 tests (none mention tampering) | **Two precise integrity cases:** a direct modification of an issue row is refused by its append-only guard; missing, inconsistent or unprovable source data gives `integrity_mismatch` before rendering |
| Section 8 open items: PD-11 research needs permission; `ManageDocuments` escalation pending | PD-11 research is **authorized** (official US federal sources and clearly identified state examples; the research note is independently reviewed before prescription-content code). `ManageDocuments` defaults are resolved in brief revision 2 (Admin and Office Manager) |

## Gates (unchanged in kind, restated per the review)
- **S1 to S3** (documents, permissions, backup and status): after the brief and permission design are approved.
- **S4 to S8** (PDF): only after this proposal is approved, the release-store provenance is approved and the folders are created, and, for prescription content fields, the PD-11 research note is approved.

**Still not authorized:**
- creating the release-store folders;
- adding Typst or the runtime;
- installer changes, migrations, or any implementation slice.
