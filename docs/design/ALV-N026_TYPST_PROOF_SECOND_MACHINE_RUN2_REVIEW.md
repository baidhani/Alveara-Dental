# ALV-N026 Typst second-machine run 2 — independent review response

**Decision:** APPROVED as evidence for the stated Typst determinism and controlled-boundary claims.  
**Reviewed evidence:** `docs/design/ALV-N026_TYPST_PROOF_SECOND_MACHINE_RUN2.md` and `docs/design/evidence/ALV-N026-typst-kit-R05/vm-run2-results-PacificStandardTime.json`.

## Accepted evidence

The approved R05 ZIP checksum was verified before extraction. The proof ran on the same separately installed, clean Pacific-time Windows VM used by Run 1, without installing the Visual C++ Redistributable and with network disabled by the operator before extraction and during the run.

The returned JSON matches the approved R05 evidence:

- exact Typst `0.15.1 (9dfd3a08)` binary hash;
- the approved app-local runtime DLL version and hash;
- both approved Noto font hashes;
- all template, common-template, and synthetic-input hashes;
- fixed issue timestamp and controlled execution configuration;
- successful exact-version engine preflight;
- all five negative controls refused by a running Typst process with their expected error classifications and no produced PDF;
- all four representative PDF types rendered twice, byte-identically, and matched the author-machine expected hashes and byte sizes.

This supports byte-identical output for the tested pinned Typst binary, pinned runtime, bundled fonts, representative templates, and fixed timestamps across the author machine in Central time and the clean VM in Pacific time.

## Boundaries that remain explicit

This proof does not establish that Typst can never make a direct network connection. It establishes that the controlled external-package import was refused under the configured empty package/cache directories and closed proxy, and that the VM had no network connection for this run. Network restrictions remain defense in depth for the later product implementation.

It also does not approve app-local deployment of the Microsoft runtime in DentalKeep. The runtime was app-local only to prove a self-contained clean-VM kit. The production PDF proposal must use the owner-directed installer-managed official Microsoft Visual C++ Redistributable strategy and document readiness/failure behavior.

## Authorized next step

The agent may revise the PDF-library proposal as its next **design-only** action. The revision must incorporate this accepted evidence, identify Typst plus its runtime prerequisite accurately, preserve the installer-managed production-runtime decision, and retain the remaining open approval items. No Typst dependency, product code, installer change, template migration, or PDF implementation is authorized until that revised proposal receives independent approval.
