# ALV-N026 final design review — proposal R05, brief revision 3, and PD-11 note revision 2

**Decision:** APPROVED, with the precise wording corrections below required before S6 prescription-template code begins.  
**Reviewed documents:**

- `ALV-N026_PDF_LIBRARY_PROPOSAL-R05.md` with its R04 base;
- `ALV-N026_PRE_IMPLEMENTATION_BRIEF.md` revision 3;
- `ALV-N026_PD-11_RESEARCH_NOTE.md` revision 2.

## Gate rulings

| Gate | Decision | Authorized scope |
|---|---|---|
| G1 — PDF engine/dependency design | **APPROVED** | Create the owner-approved versioned release-artifact folders and proceed with PDF slices only under the remaining gates below. Typst remains pinned to the approved reviewed version; product deployment uses the installer-managed Microsoft runtime, not the proof kit's app-local DLL. |
| G3 — brief and `ManageDocuments` permission design | **APPROVED** | Begin S1–S3: document classification/permissions, document UI fixes, backup alerts, and System Status. |
| G2 — PD-11 layout research | **APPROVED for the stated product-layout scope**, subject to the two wording corrections below | Begin S6 prescription content only after those exact corrections are committed. This is a general U.S.-oriented layout decision, not legal/compliance certification. |
| G4 — release-store provenance | **PENDING the approved folder creation/check** | Required before engine files enter the build for S4–S8. |

## Approved design points

- `ManageDocuments` defaults only to Admin and Office Manager; Dentist receives `ImportDocuments` only by default.
- Clinical category mapping is conservative: Correspondence and Other are Clinical; unknown/practice-added categories default Clinical.
- Import requires both `ImportDocuments` and the selected category's applicable read right. Reclassification remains practice configuration under `ManageDocumentSettings`.
- New issue records are append-only, join the established merge-scope design with direct SQL proof, and do not store generated PDFs.
- Fidelity is judged by historically accurate source data, required visible fields, correct calculations, warnings, template/version selection, and rendering/layout—not byte-identical PDF output or PDF hashes.
- The p2 layout uses actual profile/patient data, does not invent structured medication data, keeps credential/license/NPI optional, requires patient address as a DentalKeep completeness rule, and does not claim controlled-substance detection or legal compliance.

## Required PD-11 wording corrections

Make these exact design-document corrections before S6 prescription-template code. No further independent review is required for these wording-only changes.

1. Replace the p2 boundary-statement phrase:

   > “and is not printed on tamper-resistant paper”

   with:

   > “DentalKeep does not supply or verify tamper-resistant paper.”

   This avoids a false statement when a practice happens to print onto special paper. The official CMS letter supports the limited Medicaid payment-condition discussion but does not make DentalKeep responsible for the physical stationery. [CMS SMDL #07-012](https://www.medicaid.gov/federal-policy-guidance/downloads/SMD081707.pdf)

2. In the research findings, replace the categorical statement “No federal content list for ordinary prescriptions was found” with:

   > “This design research did not identify a single federal content list for ordinary non-controlled prescriptions. DentalKeep makes no federal or state compliance claim.”

   Also label the 2025 annual CFR material as an official **reference source**, not a current-law validation. The field choices remain product-layout choices; controlled-substance rules are never presented as ordinary-prescription requirements.

## Execution boundary

Focused tests only, under the existing five-minute policy, are authorized for each approved slice. Broad/full regression remains deferred to ALV-N028. Any material code, schema, permission, security, or evidence deviation still requires the normal handoff/review process.
