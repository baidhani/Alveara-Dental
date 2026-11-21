# ALV-N026 PDF proposal R04 and pre-implementation brief — independent review

**Decision:** CHANGES_REQUIRED before implementation.  
**Reviewed documents:**

- `docs/design/ALV-N026_PDF_LIBRARY_PROPOSAL-R04.md`
- `docs/design/ALV-N026_PRE_IMPLEMENTATION_BRIEF.md`

## Accepted direction

The Typst 0.15.1 / Noto Sans 2.015 / PDF/A-2b selection remains acceptable in principle. The revised proposal correctly follows the owner decision that byte-identical PDF output is not a product requirement. The new standard is historically accurate, version-faithful content regeneration using controlled source data, with no accumulating generated-PDF store.

The installer-managed Microsoft Visual C++ Redistributable direction, readiness preflight, failure behavior, release-store provenance controls, server-side PDF flow, temporary render-folder controls, and the D1–D4 material rules are acceptable foundations. No dependency or code integration is authorized until this review's corrections are made.

## Required brief corrections

### 1. Resolve A1: do not grant `ManageDocuments` to Dentist by default

The approved default is:

- **Admin and Office Manager:** `ManageDocuments`;
- **Dentist:** `ImportDocuments` by default (U-008) but **not** `ManageDocuments` by default;
- **Hygienist and Assistant:** no default `ImportDocuments` or `ManageDocuments` grant;
- existing per-practice overrides remain untouched.

`ImportDocuments` permits import only. It does not imply edit, inactivate, reactivate, or document-category administration.

### 2. Resolve A2: retain the stated conservative category mapping

Use this mapping in the forward migration:

| Classification | Built-in categories |
|---|---|
| Clinical | Radiograph, Photo, Referral letter, Medical clearance, Lab report, Correspondence, Other |
| NonClinical | Insurance card, Identification |

Practice-added and otherwise unrecognized categories default to `Clinical` until explicitly classified. This is intentional privacy protection, not an unresolved owner decision.

Changing a category's classification is practice configuration, not ordinary document management. Require the existing practice-configuration administration permission for that action, audit it, and keep `ManageDocuments` separate.

### 3. Close the import authorization rule

Importing into a category must require both:

- `ImportDocuments`; and
- the applicable read right for the selected category (`ViewClinicalDocumentation` for Clinical, otherwise `ViewDocuments`).

An importer may not upload a document into a category they are not allowed to read. Direct API tests must cover this rule.

### 4. Make merge-scope behavior an approved, tested implementation detail

The new patient-owned issue tables may join the established merge-scope pattern. This is approved only with the established guard/stand-aside behavior and direct database proof that:

- an ordinary direct attempt to alter protected patient scope is refused; and
- an approved patient merge reassigns the issue rows correctly without weakening their append-only protection.

Record the exact triggers, guard codes, and direct-SQL tests in the migration slice. This is no longer an open policy decision, but it remains a required implementation proof.

### 5. Align integrity tests with the no-byte-equivalence decision

Do not make PDF hashes or canonical-content hashes mandatory acceptance checks. A canonical-content hash may be used as an optional diagnostic only.

Replace the vague “tampered issue row gives `integrity_mismatch`” test with two precise cases:

- direct modification of an issued row is refused by the append-only database guard; and
- missing, inconsistent, or unprovable versioned source data causes `integrity_mismatch` before regeneration.

The fidelity tests must assert historical values, required warnings, calculations, version choice, and rendered text/layout—not raw PDF bytes.

### 6. PD-11 research is now authorized, with a narrow boundary

The agent may perform the design research needed for G2 using official US federal sources and clearly identified state examples only. It must document exact sources, field-by-field basis, exclusions, and the statement that the product is not claiming universal federal/state compliance. No prescription-content code is authorized until that research note is independently reviewed.

## Required R02 brief/proposal update

Revise both documents to incorporate the five corrections above, resolve A1/A2/merge-scope as stated, and change the gate table accordingly. The R02 update is design-only. Do not create release-store folders, add Typst, add the runtime, modify the installer, create migrations, or start any implementation slice yet.

After the revised brief is approved, implementation may begin only under its gates:

- S1–S3 after the brief/permission design approval;
- S4–S8 only after the PDF proposal, release-store provenance, and applicable prescription research gate are approved.
