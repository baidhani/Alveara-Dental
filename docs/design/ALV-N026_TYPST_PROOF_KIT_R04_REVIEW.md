# ALV-N026 Typst proof kit R04 — independent review response

**Decision:** CHANGES_REQUIRED. The repeat clean-VM proof is not yet authorized.  
**Reviewed artifact:** `ALV-N026_typst-proof-kit-R04.zip`  
**Artifact SHA-256:** `A4B47F4F052CAB27DA0321F41C9575B87CA78EC8975A512867000C5E1615D9F7`

## What R04 resolved

R04 makes the expected implementation changes from the Run 1 review:

- a pinned, app-local `vcruntime140.dll` is supplied next to the unchanged approved Typst binary;
- the runtime DLL is manifest-hash-pinned and its file version is checked;
- a mandatory `typst.exe --version` preflight runs before lint, controls, or rendering, and requires the exact reviewed Typst version;
- a failed process startup becomes `ENGINE_START_FAILED` rather than a false successful negative control;
- negative controls now require Typst to start and produce the expected Typst refusal characteristics;
- R03's engine, fonts, templates, inputs, expected outputs, and notices are unchanged. R04 adds only the runtime DLL and its notices, plus the runner/README/manifest changes needed for the runtime and preflight.

## Blocking gaps

### 1. Redistribution provenance has not been established

`notices/MSVC-RUNTIME-SOURCE.txt` expressly says its redistribution basis is “NOT YET VERIFIED.” That is not sufficient for a component intended to ship with the product or even to authorize the intended proof package distribution.

Microsoft permits app-local redistribution only subject to the applicable Visual Studio license terms and only for a validly licensed Visual Studio user. Microsoft also notes that app-local deployment is not generally recommended because it is not serviced automatically. See:

- https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files?view=msvc-170
- https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution
- https://learn.microsoft.com/en-us/cpp/windows/choosing-a-deployment-method?view=msvc-160

For R05, provide a concise provenance record that identifies the exact official Microsoft redistributable artifact or Visual Studio distributable directory, source/version, acquisition date, applicable redistribution-list entry, and an affirmative confirmation that the release owner is entitled to redistribute that unmodified file. Retain the source artifact hash and the shipped DLL hash. Do not characterize a pointer to `Redist.txt` as the license evidence itself.

### 2. Source/version discrepancy must be resolved

The source note says the DLL came from an MSVC directory identified as `14.44.35112`, while the DLL itself is recorded as file version `14.44.35211.0`. This may have an ordinary explanation, but it is unexplained provenance ambiguity.

R05 must reconcile it with evidence from the actual official source artifact/directory. The final record must consistently state the source package/build version, the shipped file version, architecture, Authenticode result, size, and SHA-256.

### 3. The claimed minimal dependency set is asserted, not evidenced

The note asserts that `typst.exe` needs only `VCRUNTIME140.dll` beyond Windows system components, but the raw dependency inspection is not included.

Provide the actual dependency-inspection command and its complete output for the approved Typst binary, then classify each imported library as Windows-provided, Universal CRT-provided, or separately shipped. If any additional redistributable DLL is required, include it in the same provenance, manifest, and preflight design.

### 4. Required author-machine R04 evidence is missing

The Run 1 review required an R04 author-machine execution before closure review. Submit its generated result JSON and console result. It must show:

- successful exact-version preflight;
- all five negative controls refused by a running Typst process, with expected classifications;
- all four templates byte-identical across their two renders and matching the pinned expected hashes.

## R05 boundary

R05 may update only the runtime provenance record/notices, manifest if source-derived runtime artifacts change, and proof evidence needed to resolve the four gaps above. It may not alter Typst, fonts, templates, inputs, expected output hashes, product code, installers, migrations, or application configuration.

After R05 is approved, the repeat run may occur on the same clean Pacific-time VM without installing the Visual C++ Redistributable or any other software. The larger PDF-library proposal must separately address ongoing runtime servicing; a successful proof kit does not decide the production installer strategy.
