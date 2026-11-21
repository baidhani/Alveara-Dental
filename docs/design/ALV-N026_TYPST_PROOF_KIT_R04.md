# ALV-N026 Typst proof kit R04: app-local runtime and startup preflight (response to the second-machine run 1 review)

**Date:** 2026-11-21. **Status:** AWAITING_REVIEW (closure review against the R04 criteria in the run 1 review). Not distributed, and not run on the VM.

**Artifact:** `ALV-N026_typst-proof-kit-R04.zip`, with 22 entries.

**ZIP SHA-256 (the separately stated checksum for the operator):**
```text
A4B47F4F052CAB27DA0321F41C9575B87CA78EC8975A512867000C5E1615D9F7
```

## 1. Dependency identification (review item 1)
**Method.** On the author machine, a short PowerShell script read the PE headers of the approved `typst.exe` (SHA-256 `081217a4…b31e`, unchanged). It walked the import directory (data directory 1) and the delay-import directory (data directory 13), and printed every DLL name. The same method was then applied to the runtime DLL chosen below.

**Result for `typst.exe`** (x64, `machine=0x8664`, PE32+). There are no delay imports. The 17 imported DLLs are:

| Imported DLL | Provided by |
|---|---|
| `bcryptprimitives.dll`, `kernel32.dll`, `shell32.dll`, `ole32.dll`, `crypt32.dll`, `secur32.dll`, `ntdll.dll`, `ws2_32.dll`, `api-ms-win-core-synch-l1-2-0.dll` | Windows itself |
| `api-ms-win-crt-math/string/convert/heap/runtime/stdio/locale-l1-1-0.dll` (7) | The Universal C Runtime, part of Windows 10 and later |
| **`VCRUNTIME140.dll`** | **The Visual C++ runtime, not part of Windows.** This is the only one |

**Result for `vcruntime140.dll`:** it imports `api-ms-win-crt-runtime/string/heap/stdio/convert-l1-1-0.dll` and `KERNEL32.dll` only. It has no further non-Windows dependencies.

This confirms the run 1 inference (`STATUS_DLL_NOT_FOUND` caused by the missing VC++ runtime) from the binary itself. It is no longer taken from the build target.

## 2. The app-local runtime (review items 2 and 3)
**Minimal set:** one file, `engine\vcruntime140.dll`, placed next to `typst.exe`. Windows searches the application's own folder first. No other runtime DLL is included: `msvcp140`, `vcruntime140_1`, `concrt140` and the rest are not imported.

| Field | Value |
|---|---|
| Source | Microsoft Visual Studio 2022 Community, `VC\Redist\MSVC\14.44.35112\x64\Microsoft.VC143.CRT\vcruntime140.dll`. This is Microsoft's redistributable folder for app-local deployment. The file was copied unmodified |
| File version | 14.44.35211.0 |
| Architecture | x64 (matches `typst.exe`) |
| Size | 124,544 bytes |
| SHA-256 | `d5e4d9a3e835fa679450145d6a7d94e36573a509317111904d9b3712c30d9066` |
| Authenticode | Valid; signer `CN=Microsoft Windows, O=Microsoft Corporation` |

**In the kit:**
- `manifest.json` pins the DLL's SHA-256 in `files`.
- A new `runtimeDlls` entry pins its path, version and hash. The runner refuses the run (exit 2) on a missing file, a hash mismatch or a version mismatch.
- `notices\MSVC-RUNTIME-SOURCE.txt` records the source, version, hash, signer, why this is the only DLL, and the licence basis.
- `notices\MSVC-REDIST-LIST-POINTER.txt` is Visual Studio's installed `Redist.txt`. It names the Visual Studio 2022 Distributable Code and points to the current list at `https://aka.ms/vs/17/redist.txt`.

**Disclosures (licence):**
1. **The online Distributable Code list was not checked by the author.** A web lookup was not available in the author's session. The reviewer or the owner is asked to confirm that `vcruntime140.dll` from `Microsoft.VC143.CRT` is on that list. The notice in the kit says that this is unverified.
2. **The file came from a Visual Studio *Community* installation.** That is acceptable for this scratch evidence kit. Whether DentalKeep may ship it under the Community terms, or should take it from the official `vc_redist.x64` package or a licensed Visual Studio edition, is a **product compliance question for the PDF proposal**. This kit does not decide it.

## 3. Startup preflight (review item 4)
Immediately after the manifest and version checks, before the template lint, the controls and the renders, the runner:
1. starts `typst.exe --version` (stdout and stderr are drained, with a 30-second timeout);
2. records the exit code, any start error (Windows refusing to create the process is recorded by class only, as `process_start_refused`), any timeout, the printed version and the expected version;
3. requires all of the following: exit code 0, and exactly `typst 0.15.1 (9dfd3a08)` printed.

On any failure the run writes the report with `overall = ENGINE_START_FAILED` and the `preflight` block, and exits with code 4. No lint, control or render result is produced.

## 4. Tightened negative-control rule (review item 5)
A control now counts as refused only when **all** of these hold:
- no PDF is produced;
- the exit code is exactly 1 (Typst's own error exit);
- the error class equals the control's expected class.

| Control | Expected class |
|---|---|
| External `@preview` package import | `package_unavailable` |
| Unapproved file, absolute path | `absolute_path_refused` |
| Unapproved file, relative escape | `path_escapes_root` |
| Bundled font read as data | `absolute_path_refused` |
| Unapproved file as JSON input | `absolute_path_refused` |

The following no longer count as refusal: a generic non-zero exit (`other_error`), a timeout, or a process-load failure such as `0xC0000135`. Each control's result also records `expectedErrorClass`.

## 5. Unchanged (review item 6)
Compared with R03, these are byte-for-byte unchanged: `engine\typst.exe`, both fonts, all five templates, all four inputs, the three original notices and `expected-outputs.json`. The R03 checks are also unchanged: the manifest check, the import lint, the per-render isolation and environment, the double render, and the mandatory expected-hash comparison.

**Changed-file inventory (R03 to R04):**

| File | Change |
|---|---|
| `engine\vcruntime140.dll` | Added |
| `notices\MSVC-RUNTIME-SOURCE.txt` | Added |
| `notices\MSVC-REDIST-LIST-POINTER.txt` | Added |
| `manifest.json` | Changed: 3 new file entries, the `runtimeDlls` version pin, and new hashes for the runner and README (21 pinned files) |
| `run-proof.ps1` | Changed: R04 label, DLL version check, startup preflight, tightened control rule, shared report writer |
| `README.txt` | Changed: R04 labels, "do not install the VC++ Redistributable", and the third result `ENGINE_START_FAILED` |

## 6. Author-machine verification (review item 7)
The author machine is on Central Standard Time (UTC-06:00), Windows 10.0.26300, PowerShell 5.1.

| Run | Result |
|---|---|
| **R04 kit as built** | **PASS**, exit 0, in 12 s. Preflight: exit 0, `typst 0.15.1 (9dfd3a08)`. All 5 controls were refused by Typst with exit 1 and the expected class. All 4 templates were identical across runs and matched the expected hashes (unchanged from R03) |
| Tamper A, on a throwaway copy: `vcruntime140.dll` deleted | **REFUSED** (`missing engine\vcruntime140.dll`), exit 2 |
| Tamper B, on a throwaway copy: `typst.exe` replaced by a non-executable file, with the manifest forged to match | **ENGINE_START_FAILED**, exit 4. Preflight `startError = process_start_refused`, and the report contains no controls or results |
| Tamper C, on a throwaway copy: the `runtimeDlls` version pin altered | **REFUSED** (`version mismatch engine\vcruntime140.dll`), exit 2 |

**Fix made during verification:** the first tamper A attempt failed unsafely. It failed, but with a PowerShell error (exit 1) instead of a clean refusal, because the version check read the DLL after the manifest check had already flagged it as missing. The version check now skips a file that is already reported as missing. Tamper A was rerun, giving the result above, followed by a clean PASS run.

The throwaway copies were deleted. The local results file was regenerated by the final clean run.

**What the author machine cannot show:** this machine has the VC++ runtime installed system-wide, so the PASS here does not prove that the app-local copy is enough on its own. Windows loads the app-local DLL first, but only the clean VM can prove self-sufficiency. Tamper B shows that a start failure ends the run as `ENGINE_START_FAILED`; the real `0xC0000135` case is observed on the VM.

## 7. Next run (after approval)
- The **same** clean Pacific-time VM, **unmodified**: the VC++ Redistributable is not installed, and networking is disabled before extraction.
- Verify the R04 ZIP SHA-256 above before extracting.
- Run only `powershell -NoProfile -ExecutionPolicy Bypass -File .\run-proof.ps1`.
- Return `results-PacificStandardTime.json` and the console result.
- Any result other than PASS stops the proof for review.

No product dependency, installer change, application code or migration is part of R04. The PDF proposal still has to be corrected to describe the actual deployment model: the pinned Typst executable plus its reviewed app-local runtime. That correction will be made in the proposal, not here.
