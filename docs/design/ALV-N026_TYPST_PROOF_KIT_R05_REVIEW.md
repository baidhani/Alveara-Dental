# ALV-N026 Typst proof kit R05 — independent review response

**Decision:** APPROVED for one repeat proof run on the same clean, separately installed, non-Central-time-zone Windows VM.  
**Reviewed artifact:** `ALV-N026_typst-proof-kit-R05.zip`  
**Approved ZIP SHA-256:** `E39D72388E5A276BAC6E84793CCEA4F96F3F6C4B6364CBDA7E776CE48818D6AA`

## R05 resolution

R05 resolves every blocking item from the R04 review:

- The exact Microsoft Visual C++ runtime provenance is recorded. The shipped x64 `vcruntime140.dll` is byte-identical to the payload in the identified official `vc_redist.x64.exe` artifact, and its file version, Authenticode status, architecture, size, and SHA-256 are recorded.
- The set-directory/file-version distinction is reconciled: the redistributable set is `14.44.35112`, while the official package and contained runtime file report `14.44.35211.0`.
- The redistribution record identifies the applicable Microsoft Visual Studio 2022 distributable-list entry and records the release owner's stated entitlement to distribute the unmodified runtime file. It also correctly preserves the servicing caveat: this proof-kit app-local runtime is not a decision to use app-local deployment in the product.
- Raw PE import evidence is present for both `typst.exe` and `vcruntime140.dll`. It supports the minimal one-DLL app-local kit set on Windows 10 or later; no additional non-Windows redistributable DLL is shown.
- The author-machine R05 result is `PASS`: exact engine version preflight succeeded, all five controls were refused by a running Typst process with their expected classifications, and all four representative PDFs were byte-identical across independent renders and matched their pinned expected hashes.

## Authorized repeat-run procedure

1. Use the same clean, separately installed Pacific-time VM from Run 1. Do not install the Visual C++ Redistributable or any other software.
2. Disable network access before extracting the ZIP and retain it disabled for the run.
3. Before extraction, calculate the ZIP SHA-256 and require an exact match to the approved checksum above. Stop on any mismatch.
4. Run only:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\run-proof.ps1
   ```

5. Return the console result and `results-PacificStandardTime.json` only.

Any outcome other than `OVERALL: PASS`, including `ENGINE_START_FAILED`, stops for independent review. Do not alter the VM or the proof kit to work around a failure.

## Review boundary

This authorizes an evidence run only. It does not approve Typst product integration, application-local runtime deployment in DentalKeep, installer changes, templates, production fonts, or PDF implementation. The product PDF proposal must still choose and document the installer-managed Microsoft Visual C++ Redistributable strategy separately.
