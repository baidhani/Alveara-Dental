# ALV-N026 Typst proof kit R03 — independent review response

**Decision:** APPROVED for the limited second-Windows-machine, different-time-zone proof.  
**Reviewed artifact:** `ALV-N026_typst-proof-kit-R03.zip`  
**Approved ZIP SHA-256:** `54BD140153AAB992A6DE2A5E7F0F597390B26E0E331DEB6970CAD4388D5C175E`

## Scope of this approval

This approval authorizes only the evidence run described in the kit README. It does **not** approve Typst integration, a product dependency, production templates, fonts, installer changes, migrations, or any production PDF implementation.

## R03 verification

R03 resolves the R02 closure requirement:

- the distributed runner has no `-WriteExpected` parameter;
- it contains no path that writes `expected-outputs.json` or any other file in the kit folder;
- expected-output comparison is unconditional: a missing, altered, or different expected hash fails the run;
- the kit still verifies all contained pinned files against its manifest;
- the prior external-package and file-boundary negative controls remain present;
- the archive differs from R02 only in `README.txt`, `run-proof.ps1`, and the corresponding `manifest.json` entry. The reviewed engine, fonts, templates, inputs, notices, and expected output data are unchanged.

## Required run procedure

1. Before extraction, calculate the R03 ZIP SHA-256 and compare it to the approved checksum above. Stop immediately on any mismatch.
2. Run on a separate Windows machine configured for a time zone other than Central time.
3. Extract the verified ZIP and run only:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\run-proof.ps1
   ```

4. Return the generated `results-<TimeZoneId>.json` file and the console result. Do not return any unrelated machine logs.
5. Any non-`PASS`, unexpected network-related result, hash mismatch, or negative-control failure stops the proof for review.

## Next review point

The independent reviewer must review the returned second-machine evidence before the PDF-library proposal can advance. A passing proof supports the Typst selection decision; it does not by itself authorize implementation.
