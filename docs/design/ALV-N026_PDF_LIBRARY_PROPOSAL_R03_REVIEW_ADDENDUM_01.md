# ALV-N026 PDF library proposal R03 review — Addendum 01

**Decision:** This addendum supersedes the mandatory-issued-PDF-artifact requirement in section “Blocking design contradiction: historical document preservation” of `ALV-N026_PDF_LIBRARY_PROPOSAL_R03_REVIEW.md`.

## Owner direction accepted

DentalKeep will **not** retain every generated PDF merely to support later printing. The authoritative clinical, financial, prescription, and document data remains in the database. The PDF renderer regenerates a document from that controlled data when needed.

This avoids an unnecessary accumulating PDF store and its associated storage, backup, retention, and duplicate-data cost.

## Required replacement design: versioned regeneration

The next proposal/brief must implement a regeneration contract instead of an issued-PDF archive:

1. Each issued document records the template version/hash, document issue timestamp, canonical-content hash, and engine/font/runtime bundle identity.
2. Regeneration must obtain its content from immutable or versioned source records—not from mutable current patient/practice fields that could make an old document silently change. Existing audited revisions/history may be used only after the implementation survey proves they retain every needed value at the document's issue time.
3. If an existing controlled source record cannot reconstruct a required historical value, store the smallest necessary immutable, access-controlled canonical render-data record in the database. Do not store an unrestricted full PDF copy merely for that purpose.
4. Before regeneration, recreate the canonical data and require its hash to match the recorded issuance hash. A mismatch must refuse with `integrity_mismatch`; it must not produce a document from changed current data.
5. The re-generated document must retain the original document issue date and template version. If it is generated after the original issue date, the UI/output may identify it as a regenerated copy; it must never imply that its file bytes are the preserved original browser/PDF file.

## Determinism and runtime-update rule

The system may promise byte-identical output only while rendering with the same approved engine, runtime, fonts, template, and fixed canonical input bundle.

Because the production Microsoft Visual C++ runtime is centrally serviced, DentalKeep must **not** promise perpetual byte-for-byte reproduction across later runtime versions without retaining the original rendering bundle or artifact. Instead:

- every release and approved runtime/engine/font/template change runs the golden-file gate;
- the new renderer must produce a semantically equivalent document from the same canonical source data, with required visible content/layout checks;
- the issuance record retains the original bundle identity and records the bundle used for a regenerated copy;
- any content-hash mismatch, unproven source reconstruction, or material layout/content difference stops for review.

## Legacy and status rules

The previously approved legacy distinction remains:

- pre-PDF browser-print records are not promised byte-for-byte;
- a legacy reprint is regenerated only when its historical canonical content can be verified, and is labelled as a regenerated copy;
- the `p1` prescription no-hash case must be explicitly resolved as either a proven reconstruction source or a refusal;
- a cancelled/discontinued/superseded prescription record copy is a new controlled rendering based on the appropriate status-at-copy rule and must not overwrite historical issuing data.

## Review boundary

This addendum does not authorize implementation. The ALV-N026 pre-implementation brief must map the exact database source/version for every printed material—prescription, treatment-plan patient copy, statement, receipt, and clinical record copy—and prove that its regeneration inputs meet this contract.
