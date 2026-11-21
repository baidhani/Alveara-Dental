# ALV-N026 revised PDF proposal, brief, and PD-11 research — independent review

**Decision:** CHANGES_REQUIRED before implementation approval.  
**Reviewed documents:**

- `ALV-N026_PDF_LIBRARY_PROPOSAL-R05.md`
- `ALV-N026_PRE_IMPLEMENTATION_BRIEF.md` (revision 2)
- `ALV-N026_PD-11_RESEARCH_NOTE.md`

## What is accepted

The PDF architecture is accepted in principle:

- server-side Typst, Noto Sans, and PDF/A-2b;
- no byte-for-byte PDF-output product requirement;
- no accumulating generated-PDF store;
- historically accurate, version-faithful regeneration from controlled source data;
- installer-managed central Microsoft Visual C++ Redistributable, with readiness preflight;
- the resolved `ManageDocuments` defaults, conservative category mapping, import/read rule, and merge-scope test requirement;
- the D1–D4 issue/regeneration model, subject to the prescription-scope corrections below.

No dependency, release-store folder, migration, installer change, or implementation slice is authorized until this response is incorporated.

## Required corrections to the proposal and brief

### 1. Advance all R04 references to R05

The pre-implementation brief still names `ALV-N026_PDF_LIBRARY_PROPOSAL-R04.md` in its inputs and G1. Update those references to R05. G1 may not be marked approved until the corrected R05/brief package is approved.

### 2. Preserve the no-hash policy consistently

The financial-document contract may retain an existing content hash as internal source-verification evidence, but no PDF-byte hash or canonical-content hash may become an implementation acceptance gate. The result must be judged by historically correct source values, selected version, required visible fields, calculations, warnings, and rendered content/layout.

### 3. Narrow the prescription terminology

Replace “US federal/common baseline” wherever it suggests a legal prescription standard with:

> **General U.S.-oriented prescription layout; not a federal or state compliance profile.**

The printed boundary statement must make the same limitation clear. DentalKeep must not claim that the layout is compliant for every US jurisdiction, payer, or pharmacy.

## PD-11 research corrections

### 4. Meet the authorized source boundary

The research authorization requires official US federal sources and clearly identified state examples. Cornell LII and FindLaw may be useful reading, but they are not the approved primary-source basis.

Replace or independently corroborate the legal citations using official sources before relying on them for product decisions:

- current eCFR/DEA source for 21 CFR 1306.04 and 1306.05;
- official U.S. Code/Statutes source for 21 U.S.C. 353(b), as applicable;
- an official California Legislature source for the state example, or remove California as a legal basis if its official text cannot be obtained.

The CMS Medicaid Director letter is an official source and its text is available. It supports the limited statement that written, non-electronic Medicaid prescriptions have tamper-resistant-pad conditions in the described payment context; it must not be inflated into a universal prescription-content rule. See [CMS SMDL #07-012](https://www.medicaid.gov/federal-policy-guidance/downloads/SMD081707.pdf).

### 5. Do not use controlled-substance rules as a non-controlled legal baseline

21 CFR 1306.05 applies to controlled substances. Its content list may inform a conservative **common-practice layout**, but the note must not present it as federal content requirements for ordinary non-controlled prescriptions.

The research note must clearly distinguish:

- fields included because DentalKeep chose a useful general layout;
- fields required only for controlled substances;
- fields shown only as one state example; and
- fields that are optional product/profile data.

### 6. Resolve the four PD-11 design decisions

Apply these decisions in the corrected note and brief:

1. **Credential:** optional. Display it when configured; do not refuse a print solely because it is absent. Do not print a blank credential label.
2. **License identifier, issuing jurisdiction, and NPI:** optional profile fields, displayed only when present. NPI may receive only basic ten-digit validation until an official algorithm source is documented; it is not a general printed-prescription requirement.
3. **Patient address:** keep it as a product completeness requirement for this general layout. If it is absent, refuse the controlled PDF with `print_incomplete` rather than silently issuing an incomplete layout. This is a DentalKeep quality rule, not a claim that one federal rule governs all ordinary prescriptions.
4. **Strength/dosage form limitation:** accepted for ALV-N026. Render medication text exactly as recorded; do not use separate “Strength” or “Dosage form” fields/labels that imply structured data DentalKeep does not have. Structured medication data remains deferred.

### 7. Controlled-substance and signature boundary

ALV-N026 does not add controlled-substance classification, DEA validation, e-prescribing, or state controlled-substance workflows. It must not claim it detects whether a medication is controlled.

Every p2 layout must therefore state that it is not designed or validated as a controlled-substance prescription form. A manual signature line may be printed, but the product must not describe manual signature as a universal federal requirement for ordinary prescriptions.

The product may state that it does not supply tamper-resistant paper. Do not quote or rely on further Medicaid/legal conclusions until the corrected primary-source note is independently approved.

## Required next deliverable

Submit a design-only R03 package consisting of:

1. corrected R05 references in the brief and gates;
2. a corrected PD-11 note with official-source citations, field classifications, and the decisions above; and
3. corresponding corrected p2 template/field requirements and tests.

No further engine proof is required. After independent approval of that narrow revision, S1–S3 may proceed under G3, while S4–S8 remain gated by the approved PDF/release-store conditions and S6 prescription fields remain gated by the approved PD-11 note.
