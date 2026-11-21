# ALV-N026 PDF library proposal R03 — independent review response

**Decision:** CHANGES_REQUIRED before implementation approval.  
**Reviewed proposal:** `docs/design/ALV-N026_PDF_LIBRARY_PROPOSAL-R03.md`  
**Current authorization:** Typst remains the accepted leading engine choice in principle. No product dependency, PDF code, runtime package, installer modification, template migration, or release-store creation is authorized by this response.

## Accepted R03 improvements

R03 accurately incorporates the approved R05 clean-VM proof and correctly separates the app-local proof-kit runtime from the owner-directed installer-managed Microsoft Visual C++ Redistributable production strategy. It also correctly preserves the limits of the network evidence and records the pinned binary, runtime, font, template, and PDF/A-2b evidence.

The controlled template/input boundary, pinned artifact-store concept, server-side rendering model, no-browser-print direction, and readiness-check design are acceptable foundations.

## Blocking design contradiction: historical document preservation

Sections 3, 4, and 8 make incompatible promises:

- newly issued PDFs are expected to support historical byte-for-byte preservation/rebuild;
- the product will use a centrally serviced Microsoft runtime;
- a later runtime update may change PDF bytes; and
- no issued PDF artifact or controlled historical snapshot is retained.

Once the central runtime changes, rerendering from current records cannot guarantee the original PDF bytes. Calling a changed render a “template-version or engine event” does not preserve the historical document and is not an acceptable silent substitute.

## Required R04 design

### 1. Preserve each newly issued controlled PDF as an immutable controlled document artifact

For documents first issued through the new PDF process, the issued PDF itself must be retained in DentalKeep's existing controlled document/financial-document storage model, or in an explicitly designed equivalent controlled store. This is not an unrestricted JSON snapshot store.

The R04 proposal must define:

- immutable artifact identity, issuance time, PDF SHA-256, template version, engine/font/runtime bundle id, and canonical-content hash;
- encryption/protection at rest, access permissions, audit events, retention, backup, export, merge behavior, integrity verification, and recovery;
- how an ordinary reprint retrieves the immutable issued artifact and verifies its hash;
- how a missing/corrupt artifact is handled: restore it from backup or report an integrity failure—never silently regenerate a different file as the historical original.

This resolves the central-runtime servicing issue without retaining an uncontrolled duplicate of the clinical or financial source data.

### 2. Distinguish original reprints from new record copies

For a new controlled PDF, “reprint” means retrieve the retained original PDF artifact. It does not rerender it using current patient, billing, or prescription state.

If a cancelled/discontinued/superseded prescription requires a watermarked record copy, that is a **new** issued record-copy artifact with its own issuance/audit event and status-as-of-copy. It must not alter or be represented as the original historical PDF.

Keep the owner-approved legacy rule separate: a pre-PDF document may be newly rendered only after its retained historical content hash is verified and must carry the legacy-reprint label. The R04 brief must settle the `p1` no-hash case explicitly: either define a trustworthy verification source or refuse the legacy reprint.

### 3. Correct the runtime-update claim

The issuance record should retain the render bundle identity for traceability, but historical byte preservation comes from the retained immutable PDF artifact, not from assuming a centrally updated runtime renders identical bytes forever.

The golden-file gate remains required for each DentalKeep release and for approved engine/font/template changes. A central-runtime update must be recorded and checked by readiness/golden evidence, but it may not silently rewrite, replace, or reinterpret an already issued artifact.

### 4. Tighten installer success handling

The installer may run the hash- and signature-verified official `vc_redist.x64.exe`, but process exit code alone is not proof that the required runtime is usable. After every nominal success—including `0`, `1638`, or `3010`—the installer or first application startup must run the exact Typst readiness preflight.

Do not classify `1638` as blanket success merely by assumption. Treat it as acceptable only when the subsequent preflight proves that the installed runtime can start the pinned Typst executable. A restart-required result must be surfaced clearly and followed by the same readiness validation after restart.

### 5. Close the remaining story-specific policy decisions before implementation

The ALV-N026 pre-implementation brief must state and obtain approval for:

- PD-11’s exact researched US baseline fields, official sources, exclusions, and the non-compliance disclaimer;
- the status-as-of-copy rule for prescription record copies;
- the `p1` legacy prescription no-hash rule;
- the `ManageDocuments` permission design and document-category migration; and
- the artifact-storage model above, including its retention and authorization rules.

## R04 delivery boundary

Submit a design-only R04 proposal and/or pre-implementation brief that maps every requirement above to a concrete model and test plan. Do not create the release-store folders yet and do not begin implementation.

## What remains accepted

The accepted Typst proof remains valid for its stated scope: Typst `0.15.1`, the reviewed runtime build, bundled Noto Sans 2.015, the tested templates, PDF/A-2b, fixed timestamp, two Windows machines, and two time zones. It is not a perpetual historical-rerender guarantee across future runtime updates.
