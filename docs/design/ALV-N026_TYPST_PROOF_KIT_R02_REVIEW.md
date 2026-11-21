# ALV-N026 Typst proof kit R02 — independent review response

**Decision:** CHANGES_REQUIRED — one narrow correction before second-machine execution.  
**Reviewed artifact:** `ALV-N026_typst-proof-kit-R02.zip`  
**Artifact SHA-256:** `1A97A4D2E748F3F7F4A0976C313C0F378E1A4DABDAB98772A86B3109CD3CAB56`

## R02 resolution status

R02 resolves the material issues identified in the R01 review:

- its README requires a ZIP SHA-256 verification before extraction, with the checksum to be conveyed separately;
- its template lint refuses external `#import "@..."` imports;
- it runs an external-package negative control using controlled empty package/cache folders and a closed local proxy;
- it runs multiple file-boundary negative controls, including absolute paths, relative path escape, JSON input, and an attempt to read a bundled font as ordinary data;
- its results record the binary, font, template, input, output, time-zone, PowerShell/Windows, effective controlled configuration, and negative-control outcomes without recording patient data or user-specific paths;
- its comment path has been corrected to `%TEMP%\typst-proof-results`.

The proof kit uses synthetic sample data only. No product dependency, product source, font package, template, installer, or production configuration is approved by this review.

## Required R03 correction

The distributed kit still exposes `-WriteExpected`. When invoked, it writes new expected-output hashes into `expected-outputs.json`, and the script bypasses the expected-output comparison for that invocation. This conflicts with the README's statement that the kit never writes to its own folder and weakens the kit's immutable-proof purpose.

Before the kit is sent to the second-machine operator:

1. Remove the `-WriteExpected` parameter and all code that writes `expected-outputs.json` from the distributed runner.
2. Make the expected-hash comparison unconditional. A missing, altered, or nonmatching expected hash must make the run fail.
3. If expected hashes ever need to be generated again, use a separate author-only generator script that is **not included in the distributed proof ZIP**.
4. Rebuild as R03, calculate its new ZIP SHA-256, and submit it for a quick closure review. No second-machine run is authorized for R02.

## R03 approval criterion

If R03 makes only the immutability correction above, retains all R02 controls and evidence fields, and has a newly stated archive SHA-256, it may be approved for the planned second-Windows-machine, different-time-zone proof without another design round.

## Run boundary after approval

The operator must verify the approved R03 ZIP checksum before extraction, use a Windows machine outside Central time, run only the documented command, and return the generated JSON evidence. A mismatch or any non-PASS result stops the proof for review.
