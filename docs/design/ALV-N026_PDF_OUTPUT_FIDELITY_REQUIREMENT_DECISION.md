# ALV-N026 PDF output fidelity — owner requirement decision

**Decision:** Byte-for-byte PDF equivalency is **not** a DentalKeep product requirement for any generated or regenerated document.

**Applies to:** prescriptions, treatment-plan patient copies, statements, receipts, clinical record copies, legacy reprints, and every future server-generated PDF under ALV-N026.

## Superseded requirements

This decision supersedes all ALV-N026 review/proposal language that requires, promises, gates, or treats as a failure condition:

- byte-identical historical PDF rebuilds;
- matching a previous PDF SHA-256 as a condition for a valid regenerated document;
- retaining a historical rendering bundle solely to recreate identical PDF bytes; or
- stopping implementation solely because an engine, runtime, font, or platform update changes internal PDF bytes while the rendered document remains correct.

This includes the byte-identity portions of `ALV-N026_PDF_LIBRARY_PROPOSAL-R03.md`, `ALV-N026_PDF_LIBRARY_PROPOSAL_R03_REVIEW.md`, and `ALV-N026_PDF_LIBRARY_PROPOSAL_R03_REVIEW_ADDENDUM_01.md`. Those documents remain immutable review history; this decision controls where they conflict.

## Required fidelity standard

The product requirement is **historically accurate, version-faithful document regeneration**, not file-byte equivalence.

For every issued or regenerated document, DentalKeep must ensure:

1. The patient, practice, clinical, financial, prescription, and status data shown is accurate for the document's applicable historical point in time.
2. The document uses its recorded document/template version and its applicable issue or regeneration date.
3. The source data used to regenerate historical material comes from immutable/versioned controlled records. It must never silently substitute changed current values for historical values.
4. The regenerated document contains all required fields, correct values, correct calculations, correct dates, and required status/warning text.
5. The document is auditable: issuance/reprint/regeneration action, actor, time, source/version identity, and integrity outcome are recorded without placing patient data in the PHI-free audit log.
6. If DentalKeep cannot reconstruct and verify the historical source data, it must refuse with `integrity_mismatch` or an equivalent explicit integrity failure. It must not generate a misleading document from current data.

## Verification standard

ALV-N026 tests must verify document correctness through data and rendered-content checks, not raw PDF-byte hashes:

- canonical historical source-data verification, including a canonical-content hash where suitable;
- exact assertions for patient identifiers, MRN, dates, amounts, procedure/service information, prescriber/practice information, status, watermark, and required notices;
- calculation and formatting assertions;
- template/version selection assertions;
- PDF structure/rendered-text checks and visual/layout checks that tolerate harmless internal PDF encoding or metadata differences;
- authorization, audit, error, missing-source, tamper/integrity, and legacy-reprint tests.

A deterministic byte comparison may remain an optional engineering diagnostic for a fixed engine/runtime/font/template bundle. It is not an acceptance criterion, release gate, historical-document requirement, or reason to retain generated PDFs.

## Storage and runtime consequences

DentalKeep will not retain every generated PDF merely to reproduce its bytes later. It regenerates from controlled, versioned database source data.

The centrally serviced Microsoft Visual C++ Redistributable remains the intended production runtime approach. Engine, runtime, font, template, or platform updates may change internal PDF bytes. Such a change is acceptable only when the fidelity standard above continues to pass and the document remains historically accurate and version-faithful.

## Legacy documents and status changes

- Pre-PDF browser-print material is regenerated only from verifiable historical source data and is labelled as a regenerated copy where appropriate.
- The `p1` prescription no-hash case still requires an explicit reconstruction source or an explicit refusal rule.
- A cancelled, discontinued, or superseded prescription record copy must show the applicable historical status and warning/watermark. It must not overwrite issuance history.

## Required agent action

Before implementation, revise the Typst PDF-library proposal and ALV-N026 pre-implementation brief to remove byte-equivalency acceptance language and replace it with this fidelity standard. No further cross-machine byte-determinism proof is required. The already accepted Typst proof remains useful engineering evidence only; it is not a continuing product requirement.
