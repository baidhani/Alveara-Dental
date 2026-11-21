# ALV-N026 PD-11 research note: US federal/common baseline for printed prescription content (gate G2)

**Date:** 2026-11-21. **Status:** AWAITING_REVIEW (independent review is required before any prescription-content code).

**Authorization:** the owner (2026-11-21) and brief review item 6: official US federal sources plus clearly identified state examples only.

**Scope decision governing it:** `ALV-N026_PD-11_JURISDICTION_SCOPE_DECISION.md`.

**Method:** web retrieval on 2026-11-21. "Verbatim" marks text read from the page itself. "Identified, not verified" marks a source located by search whose operative text could not be extracted in this session.

## 1. Sources
| # | Source | URL (accessed 2026-11-21) | Status |
|---|---|---|---|
| S1 | 21 CFR 1306.05, *Manner of issuance of prescriptions* (DEA; controlled substances). Page shows "as amended at 91 FR 34768, June 9, 2026" | https://www.law.cornell.edu/cfr/text/21/1306.05 (eCFR at ecfr.gov redirected and was not read) | Verbatim |
| S2 | 21 CFR 1306.04(a), *Purpose of issue of prescription* | https://www.law.cornell.edu/cfr/text/21/1306.04 | Verbatim |
| S3 | 21 U.S.C. 353(b) (FD&C Act 503(b)): prescription drugs and the dispensing label | https://www.law.cornell.edu/uscode/text/21/353 | Verbatim (excerpts) |
| S4 | CMS, *National Provider Identifier Standard (NPI)*. Last modified 08/14/2026 | https://www.cms.gov/regulations-and-guidance/administrative-simplification/nationalprovidentstand | Verbatim |
| S5 | Social Security Act 1903(i)(23) (42 U.S.C. 1396b(i)(23)): Medicaid tamper-resistant prescription pads; CMS State Medicaid Director letter SMD 08-17-07; CMS "Updates to Tamper Resistant Prescription Policy" (2008) | https://www.medicaid.gov/Federal-Policy-Guidance/downloads/SMD081707.pdf ; https://www.cms.gov/Regulations-and-Guidance/Legislation/DeficitReductionAct/Downloads/TamperUpdate.pdf | **Identified, not verified** (the PDF text could not be extracted; the statute page was not reached at (i)(23)) |
| S6 (state example) | California Business and Professions Code 4040, *Prescription; content requirements* (FindLaw copy, "current as of January 01, 2026"; the official leginfo page refused automated access) | https://codes.findlaw.com/ca/business-and-professions-code/bpc-sect-4040/ | Verbatim (secondary copy of the official text) |

**Key verbatim text:**

**S1, 1306.05(a):**
> "All prescriptions for controlled substances shall be dated as of, and signed on, the day when issued and shall bear the full name and address of the patient, the drug name, strength, dosage form, quantity prescribed, directions for use, and the name, address and registration number of the practitioner."

**S1, 1306.05(d):**
> "...paper prescriptions shall be written with ink or indelible pencil, typewriter, or printed on a computer printer and shall be manually signed by the practitioner. A computer-generated prescription that is printed out or faxed by the practitioner must be manually signed."

**S2, 1306.04(a):**
> "A prescription for a controlled substance to be effective must be issued for a legitimate medical purpose by an individual practitioner acting in the usual course of his professional practice."

**S3, 353(b)(1):** prescription drugs "shall be dispensed only (i) upon a written prescription of a practitioner licensed by law to administer such drug".

**S3, 353(b)(2), the dispensed label (the pharmacy's, not the prescription's):**
> "the name and address of the dispenser, the serial number and date of the prescription or of its filling, the name of the prescriber, and, if stated in the prescription, the name of the patient, and the directions for use and cautionary statements, if any, contained in such prescription"

**S4:**
> "The NPI is a unique identification number for covered health care providers. Covered health care providers and all health plans and health care clearinghouses must use the NPIs in the administrative and financial transactions adopted under HIPAA. The NPI is a 10-position, intelligence-free numeric identifier (10-digit number)."

**S6, 4040(a)(1).** A prescription is "Given individually for the person or persons for whom ordered that includes all of the following:"
- **(A)** "The name or names and address of the patient or patients."
- **(B)** "The name and quantity of the drug or device prescribed and the directions for use."
- **(C)** "The date of issue."
- **(D)** "...the name, address, and telephone number of the prescriber, the prescriber's license classification, and the prescriber's federal registry number, if a controlled substance is prescribed."
- **(E)** "A legible, clear notice of the condition or purpose for which the drug is being prescribed, if requested by the patient or patients."
- **(F)** "If in writing, signed by the prescriber issuing the order..."

**S5 (identified, not verified):** CMS search summaries describe:
- no federal Medicaid payment for covered outpatient drugs on written, non-electronic prescriptions not executed on a tamper-resistant pad;
- three baseline characteristics: against copying, against erasure or modification, and against counterfeit forms;
- electronic, faxed and verbal prescriptions excluded.

These points must be verified against the PDF text before they are quoted in the product or relied on.

## 2. Findings
1. **There is no single federal content list for non-controlled prescriptions.** Federal law requires a prescription-only drug to be dispensed on a prescription of a licensed practitioner (S3). The content requirements for ordinary prescriptions are set by **state** law (example S6).
2. **The only federal element list is for controlled substances (S1).** It is the commonly used "common baseline" of elements, and it largely overlaps the state example (S6).
3. **The NPI is a HIPAA transaction identifier (S4).** Neither S1 nor S6 requires it on a printed prescription. It is therefore **optional** on the print, shown only when the prescriber has one recorded.
4. **A computer-printed prescription must be manually signed** where a signature is required: S1(d) for controlled substances, and S6(F) for written prescriptions. **DentalKeep cannot sign.** It prints a signature line, and the prescriber signs by hand.
5. **Medicaid paper prescriptions need tamper-resistant pads (S5).** Plain-paper printing cannot satisfy this. It is a physical-stationery property, not print content.

## 3. Proposed baseline print fields (US federal/common baseline)
The basis column shows which source requires each field: **F** = S1 element (federal, controlled-substance list, used as the common baseline); **CA** = the S6 state example; **Opt** = optional.

| Field | Basis | Data source (actual data only; nothing invented) | Current model |
|---|---|---|---|
| Patient full name | F, CA | Patient record, captured in the `PrescriptionIssue` envelope | Present |
| Patient address | F, CA | Patient record address, in the envelope. **If missing, print is refused** (`print_incomplete`: "patient address required") | Patient address exists in registration (**to confirm in slice**); not printed today |
| Patient date of birth, MRN | Opt (identification; MRN per OQ-2) | Patient record | DOB present; MRN per ALV-N025 |
| Drug name | F, CA | `Prescription.Medication` (free text) | Present |
| Strength / dose | F ("strength") | `DoseValue` + `DoseUnit` | Present as a dose. **Limitation:** the model has no separate strength and dosage-form fields; the free-text medication must carry them (for example "Amoxicillin 500 mg capsule") |
| Dosage form | F | As above | See the limitation |
| Quantity | F, CA | `PrescriptionDetail.QuantityValue/Unit` (with F-165 plurals) | Present |
| Directions for use | F, CA | Frequency + instructions | Present |
| Refills | Common practice; not in S1(a) or S6(a)(1) | `PrescriptionDetail.Refills` | Present (kept) |
| Date of issue | F, CA | `IssuedAtUtc` as a practice-local date; the prescribed date is also shown | Present |
| Prescriber name | F, CA | Staff profile display name, in the envelope | Present |
| Prescriber address and telephone | F (address), CA (address and phone) | Practice address and phone (prescriber's practice), in the envelope | Practice present |
| Prescriber license classification (credential) | CA | **New** staff-profile field `CredentialLabel` (for example DDS, DMD), entered by an administrator | Missing |
| Prescriber license number and issuing jurisdiction | Not required by S1 or S6; widely expected | **New** optional `LicenseNumber` and `LicenseJurisdiction`, printed when present | Missing |
| NPI | Opt (S4) | **New** optional `Npi`: exactly 10 digits, printed when present. Check-digit validation is applied only if its official algorithm source is cited in the slice | Missing |
| Signature line | F (d), CA (F) | Printed line "Prescriber signature" with the date. Manual signing is required | Missing |
| Condition or purpose, if the patient requests it | CA (E) | **Excluded** from the baseline (state-specific, on request). Recorded as an exclusion | — |
| Status watermark, status date and reason (F-164) | Owner decision | `PrescriptionEvent` as of issue | Partly |
| Boundary statement | Owner decision PD-11 | Fixed template text (below) | Missing |

**Neutral naming (scope decision):** the stored concepts are credential, licence identifier, issuing jurisdiction and provider identifier. "NPI" is a print label for the US identifier only. There is no jurisdiction-profile engine.

**Proposed boundary statement (printed in small type):**
> "Printed by DentalKeep using a general US baseline layout. It is not a controlled-substance prescription form, a tamper-resistant pad or a state-specific prescription form. The prescriber is responsible for meeting the requirements of their state and of the dispensing pharmacy."

## 4. Excluded from ALV-N026 (stated in the product's help and in this record)
1. **Controlled substances:**
   - the DEA registration number on the print, Schedule II rules, multiple-prescription rules, partial fills;
   - electronic prescribing for controlled substances;
   - state security prescription forms (for example California Health and Safety Code 11162.1; **identified only, not researched**).

   The product must not present a printout as a valid controlled-substance prescription. **Proposal: the print refuses, or carries a "not for controlled substances" notice, when the prescriber flags a controlled substance.** The model has no schedule field today, so the boundary statement carries this. A schedule field is a deferred-story item.
2. **Medicaid tamper-resistant pad requirements (S5).** These are physical stationery; excluded and disclosed.
3. **State-specific additions,** such as CA 4040(a)(1)(E), state forms, and state serial numbers.
4. **Electronic transmission or e-prescribing,** and any pharmacy-network integration.
5. **Electronic or applied signatures.** Manual signature only.
6. **Any claim of compliance** with every US state or another country. That waits for the deferred jurisdiction-profiles story.

## 5. Data-model and UI impact (for the brief's S6 after G2)
- **`StaffProfile` gains four nullable fields:** `CredentialLabel`, `LicenseNumber`, `LicenseJurisdiction`, `Npi`.
  - They are edited by administrators on the staff screen, with validation (lengths, control characters, NPI = 10 digits) and an audit event.
  - They are copied into the `PrescriptionIssue` envelope at issue, so a later edit never changes an issued prescription.
- **Printing refuses with a clear message** when the patient address or the prescriber credential is missing (`print_incomplete`, naming the missing item). It never prints blanks silently.
- **The template p2** carries the fields above, the signature line, the boundary statement and the F-164 watermark.

## 6. Open points for the reviewer
1. Is the S1 element list an acceptable "common baseline" source for non-controlled dental prescriptions, given that state law governs those? (Recommended: yes, as a conservative superset, with S6 as the state example.)
2. Should printing **require** the prescriber credential (the CA example) or only show it when present? (Recommended: require it, so no prescriber prints without a classification.)
3. Should S5 be verified from the PDF text before it is mentioned in the help text? (Recommended: yes. Until then, help mentions "Medicaid tamper-resistant pads" as excluded without quoting.)
4. The strength and dosage-form limitation: is it accepted for N026, with structured fields deferred?
