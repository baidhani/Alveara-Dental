# ALV-N026 PD-11 research note (revision 2): general U.S.-oriented prescription layout (gate G2)

**Date:** 2026-11-21. **Status:** APPROVED for the stated product-layout scope (`ALV-N026_R03_FINAL_DESIGN_REVIEW.md`). Its two required wording corrections are applied in this revision (boundary statement; finding 1 and the reference-source label).

**Revision 2 answers:** `ALV-N026_R02_PACKAGE_REVIEW.md`, items 3 to 7:
- official sources only;
- controlled-substance rules are not presented as an ordinary-prescription baseline;
- each field is classified;
- the four design decisions are applied;
- the boundary on controlled substances, signatures and Medicaid is stated.

**What DentalKeep provides:**
> **General U.S.-oriented prescription layout; not a federal or state compliance profile.**

DentalKeep does not claim the layout is compliant for any US jurisdiction, payer or pharmacy.

## 1. Official sources (retrieved 2026-11-21)
| # | Source | Official location | What it supports |
|---|---|---|---|
| S1 | 21 CFR 1306.05, *Manner of issuance of prescriptions* (CFR 2025 edition, dated 2025-04-01, GPO) | https://www.govinfo.gov/content/pkg/CFR-2025-title21-vol9/xml/CFR-2025-title21-vol9-sec1306-05.xml | The element list **for controlled substances only**, and manual signing of computer-printed controlled-substance prescriptions |
| S2 | 21 CFR 1306.04(a), *Purpose of issue of prescription* (CFR 2025, GPO) | https://www.govinfo.gov/content/pkg/CFR-2025-title21-vol9/xml/CFR-2025-title21-vol9-sec1306-04.xml | Controlled-substance prescriptions only: legitimate medical purpose |
| S3 | 21 U.S.C. 353(b) (United States Code, 2023 edition, GPO) | https://www.govinfo.gov/content/pkg/USCODE-2023-title21/html/USCODE-2023-title21-chap9-subchapV-partA-sec353.htm | Prescription drugs are dispensed on the prescription of a licensed practitioner. The **dispensing label's** contents (the pharmacy's label, not the prescription's) |
| S4 | CMS, *National Provider Identifier Standard (NPI)*, last modified 08/14/2026 | https://www.cms.gov/regulations-and-guidance/administrative-simplification/nationalprovidentstand | What an NPI is, and where it is required (HIPAA standard transactions) |
| S5 | CMS State Medicaid Director Letter **SMDL #07-012**, 17 August 2007 | https://www.medicaid.gov/federal-policy-guidance/downloads/SMD081707.pdf (text extracted locally from the downloaded official PDF) | The Medicaid **payment** condition on written, non-electronic prescriptions: a tamper-resistant pad |

**About S1 and S2:** the 2025 annual CFR edition is used as an official **reference source**, not as a current-law validation. The field choices are product-layout choices. Controlled-substance rules are never presented as ordinary-prescription requirements.

**Not used as a legal basis:**
- **California Business and Professions Code 4040.** The official Legislature site (`leginfo.legislature.ca.gov`) refused automated access (HTTP 403), and the review does not accept secondary copies. The revision 1 state example is therefore **withdrawn**. Revision 2 includes no state example.
- **Cornell LII and FindLaw copies.** They are superseded by the official sources above.

**Verbatim (S1, 1306.05(a)):**
> "All prescriptions for controlled substances shall be dated as of, and signed on, the day when issued and shall bear the full name and address of the patient, the drug name, strength, dosage form, quantity prescribed, directions for use, and the name, address and registration number of the practitioner."

**S1, 1306.05(d):**
> "Where an oral order is not permitted, paper prescriptions shall be written with ink or indelible pencil, typewriter, or printed on a computer printer and shall be manually signed by the practitioner. A computer-generated prescription that is printed out or faxed by the practitioner must be manually signed."

**S2, 1306.04(a):**
> "A prescription for a controlled substance to be effective must be issued for a legitimate medical purpose by an individual practitioner acting in the usual course of his professional practice."

**S3, 353(b)(1):** a prescription drug shall be dispensed only "upon a written prescription of a practitioner licensed by law to administer such drug" (or an oral prescription reduced to writing, or a refill as authorized).

**S3, 353(b)(2):** the dispensed drug's label contains "the name and address of the dispenser, the serial number and date of the prescription or of its filling, the name of the prescriber, and, if stated in the prescription, the name of the patient, and the directions for use and cautionary statements, if any, contained in such prescription".

**S4:**
> "The NPI is a unique identification number for covered health care providers. Covered health care providers and all health plans and health care clearinghouses must use the NPIs in the administrative and financial transactions adopted under HIPAA. The NPI is a 10-position, intelligence-free numeric identifier (10-digit number)."

**S5, SMDL #07-012.** Section 1903(i)(23) of the Social Security Act states that payment shall not be made for:
> ". . . amounts expended for medical assistance for covered outpatient drugs (as defined in section 1927(k)(2)) for which the prescription was executed in written (and non-electronic) form unless the prescription was executed on a tamper-resistant pad."

The letter also says:
> "the payment limitation does not apply to e-prescriptions transmitted to the pharmacy, prescriptions faxed to the pharmacy, or prescriptions communicated to the pharmacy by telephone by a prescriber"

> "the requirement for the use of a tamper-resistant prescription pad does not apply when a managed care entity pays for the prescription"

It names three characteristics: preventing unauthorized copying, preventing erasure or modification, and preventing counterfeit forms. All three were required from 1 October 2008.

## 2. What the sources do and do not establish
1. **This design research did not identify a single federal content list for ordinary non-controlled prescriptions. DentalKeep makes no federal or state compliance claim.** Federal law requires a licensed practitioner's prescription for prescription drugs (S3). The content of ordinary prescriptions is a matter of state law, which this note does not cover.
2. **S1's element list applies only to controlled substances.** It is **not** presented as a requirement for ordinary prescriptions. DentalKeep uses it only as a reference for a sensible general layout.
3. **The NPI is a HIPAA transaction identifier (S4).** It is not shown here to be a general printed-prescription requirement.
4. **Manual signing** is required by S1(d) for computer-printed **controlled-substance** prescriptions. This note makes **no** claim that a manual signature is a universal federal requirement for ordinary prescriptions.
5. **S5 is a Medicaid payment condition** on written, non-electronic prescriptions for covered outpatient drugs, with stated exceptions. It concerns the **paper (pad)**, not the printed content, and is **not** a universal prescription-content rule.

## 3. p2 field requirements, with their classification
**Classes:**
- **L** = included because DentalKeep chose a useful general layout (product decision; S1 used only as a reference);
- **CS** = required only for controlled substances (S1); **not provided** by DentalKeep;
- **ST** = state example (none in revision 2);
- **OPT** = optional product or profile data, shown only when present.

| Field | Class | Data source (actual data; nothing invented) | Rule (review item 6) |
|---|---|---|---|
| Patient full name | L | Patient record, captured in the `PrescriptionIssue` envelope | Required |
| Patient address | L (a DentalKeep completeness rule) | Patient record address, in the envelope | **Required:** if absent, refuse with `print_incomplete` ("patient address is required for this layout"). A product quality rule, not a claim about any one federal rule |
| Patient date of birth, MRN | L | Patient record | Required (MRN per OQ-2) |
| Medication | L | `Prescription.Medication`, **rendered exactly as recorded** | **No separate "Strength" or "Dosage form" labels** (item 6.4). Structured medication data is deferred |
| Dose | L | `DoseValue` + `DoseUnit`, labelled "Dose" | Required |
| Quantity | L | `PrescriptionDetail` quantity (F-165 plurals) | Required |
| Directions for use | L | Frequency + instructions | Required |
| Refills | L | `PrescriptionDetail.Refills` | Required |
| Date of issue; date prescribed | L | `IssuedAtUtc` and `PrescribedAtUtc`, as practice-local dates | Required |
| Prescriber name | L | Staff profile display name, in the envelope | Required |
| Prescriber address and telephone | L | Practice address and phone, in the envelope | Shown when present |
| Prescriber credential (for example DDS, DMD) | OPT | **New** staff-profile `CredentialLabel` | **Optional:** shown when configured; **never refuses a print** and never prints an empty label (item 6.1) |
| Licence identifier, issuing jurisdiction | OPT | **New** `LicenseNumber`, `LicenseJurisdiction` | Shown only when present (item 6.2) |
| NPI | OPT | **New** `Npi` | Shown only when present. **Basic validation only: exactly 10 digits.** No check-digit algorithm until an official source is documented (item 6.2) |
| DEA registration number | CS | — | **Not provided.** No field and not printed |
| Signature line | L | Printed "Prescriber signature" line with the date | May be printed. Not described as a universal requirement (item 7) |
| Status watermark, status date and reason | L (owner F-164) | `PrescriptionEvent` as of issue | Watermark "<STATUS> — DO NOT DISPENSE" on inactive statuses |
| Boundary statement | L (owner PD-11; item 3 and 7) | Fixed template text (below) | Printed on **every** p2 |

**Neutral naming:** the stored concepts are `CredentialLabel`, `LicenseNumber`, `LicenseJurisdiction` and a provider identifier (`Npi`, displayed as "NPI"). There is no jurisdiction-profile engine.

**Boundary statement (every p2, small type):**
> "General U.S.-oriented prescription layout produced by DentalKeep. It is not a federal or state compliance profile, and is not designed or validated as a controlled-substance prescription form. DentalKeep does not supply or verify tamper-resistant paper. DentalKeep does not determine whether a medication is a controlled substance. The prescriber is responsible for meeting the requirements of their jurisdiction, payer and dispensing pharmacy."

## 4. Exclusions (item 7)
1. **Controlled substances:**
   - no controlled-substance classification;
   - no DEA number field or validation;
   - no EPCS (electronic prescribing for controlled substances);
   - no state controlled-substance workflow or security form.

   **DentalKeep does not claim to detect** whether a medication is controlled; every p2 says so.
2. **Tamper-resistant paper.** The product states: "DentalKeep does not supply or verify tamper-resistant paper." No further Medicaid or legal conclusion is drawn in the product.
3. State-specific requirements of any kind.
4. E-prescribing and pharmacy networks.
5. Electronic or applied signatures.
6. Any compliance claim for any jurisdiction, payer or pharmacy. That waits for the deferred jurisdiction-profiles story.

## 5. Model and UI impact (S6, after G2 approval)
- **`StaffProfile` gains four nullable fields:** `CredentialLabel`, `LicenseNumber`, `LicenseJurisdiction`, `Npi`.
  - They are edited by an administrator, with length and control-character checks (NPI exactly 10 digits) and an audit event that carries no values.
  - They are copied into the `PrescriptionIssue` envelope at issue.
- **Missing patient address:** the print refuses with `print_incomplete` and names the missing item. No other missing optional field refuses a print.

## 6. Tests (fidelity, not bytes)
**Required fields:**
- Every required field appears with the historical value (PDF text extraction).
- The medication is printed exactly as recorded, with no "Strength" or "Dosage form" label.

**Optional fields:**
- Credential, licence and NPI are shown when present.
- When they are absent: no empty labels, and the print is **not** refused.

**Refusals:**
- A missing patient address gives `print_incomplete`, and no PDF is produced.

**Fixed text:**
- The boundary statement is on every p2.
- There is no DEA field.
- No text claims compliance or controlled-substance detection.

**Validation:**
- NPI: 10 digits accepted; 9 digits, 11 digits or letters refused. No check-digit test.

**History:**
- A later profile edit (credential, licence or NPI changed) does not change a regenerated earlier p2.

**Status:**
- Inactive statuses print the watermark, status date and reason; an active status prints none.

**Audit:**
- Profile-field edits are audited without values.
