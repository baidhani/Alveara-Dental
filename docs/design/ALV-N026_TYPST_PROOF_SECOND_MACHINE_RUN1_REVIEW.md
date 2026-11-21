# ALV-N026 Typst second-machine run 1 — independent review response

**Decision:** Accept the run as **FAIL — engine runtime unavailable**. Approve the constrained R04 proof-kit remediation below.  
**Reviewed evidence:** `docs/design/ALV-N026_TYPST_PROOF_SECOND_MACHINE_RUN1.md`  
**No product implementation is authorized.**

## Finding and disposition

The R03 archive checksum was verified before extraction, the VM was fresh and separately installed, its Pacific time zone satisfied the non-Central requirement, and networking was disabled. The approved Typst executable did not start: Windows returned `0xC0000135` (`STATUS_DLL_NOT_FOUND`).

This is a valid failed proof run. It establishes a deployment prerequisite for the proposed engine. It establishes **nothing** about cross-machine determinism, time-zone independence, network/package isolation, or file-boundary enforcement because the engine never executed.

The five negative controls are explicitly **not accepted as passing evidence**. Their prior `refused=true` values resulted only from process startup failure.

## Approved R04 remediation scope

R04 may make only the following changes:

1. On the author machine, identify the actual non-Windows DLL imports for the approved `typst.exe`. Record the exact diagnostic method and the complete result. Do not infer the dependency solely from the MSVC build target.
2. Package only the required, architecture-matching, app-local Microsoft Visual C++ redistributable DLLs next to `typst.exe`. Obtain them from the official Microsoft Visual C++ Redistributable distribution and comply with its redistribution terms.
3. Add every new DLL to `manifest.json` with SHA-256 and version. Include the applicable Microsoft license/redistribution notice and source/version record in the kit.
4. Add a mandatory startup preflight before lint, controls, or rendering. It must:
   - invoke `typst.exe --version`;
   - require successful startup and the exact expected Typst 0.15.1 version;
   - record the version and exit result; and
   - terminate the run as `ENGINE_START_FAILED` if startup fails.
5. Tighten the negative-control predicate. A control passes only when the engine started and Typst itself returned the expected controlled refusal/error classification. A generic non-zero exit, timeout, or process-load failure must not count as refusal.
6. Keep the approved Typst binary unchanged and preserve its approved hash. Keep templates, inputs, fonts, expected PDF outputs, and their hashes byte-for-byte unchanged.
7. Re-run the revised kit once on the author machine, then submit the new R04 ZIP, SHA-256, changed-file inventory, and author-machine result for closure review.

## Constraints and next run

Do not install the Visual C++ Redistributable into the clean VM. After R04 approval, run the revised self-contained kit on the same VM without modifying the VM. That proves whether the kit itself supplies the required runtime.

R04 may not add a product dependency, alter the product installer, change application code, or declare Typst a one-file executable. The larger PDF proposal must be corrected to describe the actual deployment model: the pinned Typst executable plus its reviewed app-local runtime dependencies.

## R04 review criteria

R04 will be approvable for the repeat VM proof only if the package is traceable to official Microsoft redistributable artifacts, the app-local DLL set is minimal and hash-pinned, startup/version verification happens before every other result, and no unreviewed changes were made outside the constrained scope.
