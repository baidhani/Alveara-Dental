# ALV-N026 Typst proof kit R05: runtime provenance and evidence (response to the kit R04 review)

**Date:** 2026-11-21. **Status:** AWAITING_REVIEW. Not distributed and not run on the VM.

**Artifact:** `ALV-N026_typst-proof-kit-R05.zip`, with 22 entries.

**ZIP SHA-256 (the separately stated checksum for the operator):**
```text
E39D72388E5A276BAC6E84793CCEA4F96F3F6C4B6364CBDA7E776CE48818D6AA
```

**Evidence folder:** `docs/design/evidence/ALV-N026-typst-kit-R05/`.

## R05 boundary check
These are byte-for-byte unchanged from R04 (and from R03):
- the Typst binary;
- the fonts, the templates and the inputs;
- the expected output hashes;
- the original notices;
- `engine\vcruntime140.dll` (also unchanged from R04).

| File | Change from R04 |
|---|---|
| `notices\MSVC-RUNTIME-SOURCE.txt` | Rewritten as the full provenance record (gaps 1 and 2) |
| `manifest.json` | Hashes updated for the three changed files below and above. No new entries |
| `run-proof.ps1` | Kit label `R04` to `R05` only (2 lines). No logic change |
| `README.txt` | Kit label and ZIP name `R04` to `R05` only |

## Gap 1: redistribution provenance
The full record is in the kit as `notices\MSVC-RUNTIME-SOURCE.txt`. A copy is at `evidence/.../MSVC-RUNTIME-SOURCE-R05.txt`.

- **Official artifact.** The Microsoft Visual C++ Redistributable package `vc_redist.x64.exe` (version 14.44.35211.0, 25,635,768 bytes, SHA-256 `cc0ff0eb…b713b`, Authenticode Valid). It sits in the Visual Studio distributable directory `VC\Redist\MSVC\14.44.35112\`.
  - Its x64 payload `vcruntime140.dll_amd64` is **byte-identical** to the shipped DLL: SHA-256 `d5e4d9a3…9066`, 124,544 bytes.
  - It was extracted in scratch only, by carving the bundle's cabinets and running `expand.exe`. Nothing was installed.
- **Shipped file source.** The file was copied unmodified from `[VisualStudioFolder]\VC\Redist\MSVC\14.44.35112\x64\Microsoft.VC143.CRT\`.
  - The Visual Studio 2022 Community installer put it there on 2025-09-13.
  - It was copied for the kit on 2026-11-21.
- **Redistribution-list entry.** Microsoft's "Visual Studio 2022 Redistribution" page, section "Visual C++ Runtime Files" (page date 2025-11-11, accessed 2026-11-21). `https://aka.ms/vs/17/redist.txt` redirects to it.
  - It permits copying and distributing "any of the files within" `[VisualStudioFolder]\VC\redist` "and its subfolders", unmodified.
  - The only exclusions are the `debug_nonredist` folders. The shipped file's folder is not excluded.
  - This is limited to licensed Visual Studio users.
  - The installed `Redist.txt` is kept as a pointer only and is no longer presented as licence evidence.
- **Entitlement.** On 2026-11-21 the release owner affirmed that they are a validly licensed Visual Studio 2022 (Community) user, entitled to redistribute the unmodified file.
- **Retained hashes.** Source artifact `cc0ff0eb1dc3f5188ae6300faef32bf5beeba4bdd6e8e445a9184072096b713b`. Shipped DLL `d5e4d9a3e835fa679450145d6a7d94e36573a509317111904d9b3712c30d9066`.
- **Disclosure.** Neither Microsoft page names `vcruntime140.dll` or `Microsoft.VC143.CRT` individually. Coverage rests on the folder-level wording ("any of the files within … `\VC\redist` and its subfolders").
- **Servicing.** Microsoft's "Redistribute Visual C++ Files" page (2026-04-13) says app-local installation is possible but not recommended "for servicing reasons". It recommends the `vc_redist` packages. This is recorded for the PDF proposal's production runtime decision. It is not decided here.

## Gap 2: version reconciliation
`14.44.35112` is the version of the redistributable **set**. It is the directory name, and Visual Studio records the same value in `VC\Auxiliary\Build\Microsoft.VCRedistVersion.default.txt`.

`14.44.35211.0` is the build version of the **files** in that set. The official `vc_redist.x64.exe` in the same directory, and every runtime DLL in `Microsoft.VC143.CRT`, report 14.44.35211.0.

**The consistent final record:**

| Field | Value |
|---|---|
| Package and set | VC143 redistributable set 14.44.35112, package `vc_redist.x64.exe` 14.44.35211.0 |
| Shipped file version | 14.44.35211.0 |
| Architecture | x64 |
| Authenticode | Valid, Microsoft |
| Size | 124,544 bytes |
| SHA-256 | `d5e4d9a3…9066` |

## Gap 3: dependency inspection (raw output)
- **Command:** `powershell -NoProfile -ExecutionPolicy Bypass -File list-pe-imports.ps1 -Path <file>`. The script is in the evidence folder. It reads the PE import directory (data directory 1) and the delay-import directory (data directory 13), and needs no external tools.
- **Complete outputs:** `evidence/.../imports-typst.txt` and `evidence/.../imports-vcruntime140.txt`.

| `typst.exe` import (17; no delay imports) | Classification |
|---|---|
| `bcryptprimitives.dll`, `api-ms-win-core-synch-l1-2-0.dll`, `kernel32.dll`, `shell32.dll`, `ole32.dll`, `crypt32.dll`, `secur32.dll`, `ntdll.dll`, `ws2_32.dll` | Windows-provided |
| `api-ms-win-crt-math`, `-string`, `-convert`, `-heap`, `-runtime`, `-stdio`, `-locale` (`-l1-1-0.dll`) | Universal CRT, provided by Windows 10 and later |
| `VCRUNTIME140.dll` | **Separately shipped:** `engine\vcruntime140.dll` |

| `vcruntime140.dll` import (6; no delay imports) | Classification |
|---|---|
| `api-ms-win-crt-runtime`, `-string`, `-heap`, `-stdio`, `-convert` (`-l1-1-0.dll`) | Universal CRT |
| `KERNEL32.dll` | Windows-provided |

No further redistributable DLL is required.

## Gap 4: author-machine evidence (the R05 kit as built)
The author machine is on Central Standard Time (UTC-06:00).

- **Console:** `evidence/.../author-run-console.txt`, ending `OVERALL: PASS`, exit 0.
- **Result JSON:** `evidence/.../results-CentralStandardTime.json`. It records no paths or usernames.

It shows:
- **Exact-version preflight:** exit 0, `typst 0.15.1 (9dfd3a08)`, equal to the expected version.
- **All 5 negative controls refused by a running Typst:**
  - each has exit 1 and its expected class (`package_unavailable`; `absolute_path_refused` three times; `path_escapes_root`);
  - no PDF was produced and the package folders stayed empty.
- **All 4 templates byte-identical across both renders and matching the pinned expected hashes:**

  | Template | Size |
  |---|---|
  | prescription-p2 | 29,368 bytes |
  | treatment-plan-copy-v1 | 27,445 bytes |
  | statement-v4 | 27,944 bytes |
  | receipt-v4 | 24,645 bytes |

The R04 tamper results (missing DLL refused, engine start failure giving `ENGINE_START_FAILED`, version pin mismatch refused) apply unchanged, because the runner logic is unchanged.

**Disclosure about the working folder:** while building R05, the new notice was first written into the R04 working folder by mistake. That folder was restored from the R04 ZIP, which was not altered (its SHA-256 is still `A4B47F4F…D9F7`). The R05 changed-file list above was re-derived against the restored R04 content.

## After approval
- The same clean Pacific-time VM, **unmodified**: nothing installed, and the network disabled before extraction.
- Verify the R05 ZIP SHA-256 above, then run only `powershell -NoProfile -ExecutionPolicy Bypass -File .\run-proof.ps1`.
- Return `results-PacificStandardTime.json` and the console result.
- Any result other than PASS stops the proof for review.

No product dependency, installer, application code or configuration is touched.
