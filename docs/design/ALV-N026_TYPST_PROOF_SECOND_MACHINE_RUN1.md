# ALV-N026 Typst proof kit R03: second-machine run 1 (FAIL) and diagnosis

**Date:** 2026-11-21. **Status:** AWAITING_REVIEW. The proof is stopped, as the R03 review requires for any non-PASS result. Nothing has been rebuilt or rerun.

## Run conditions
- **Machine:** a fresh, separately installed Windows VM (not a clone of the author machine).
  - Microsoft Windows NT 10.0.26200.0, PowerShell 5.1.26100.7920.
  - Time zone Pacific Standard Time, UTC-08:00. This is outside Central time, as required.
- **Network:** disabled before extraction and before the run.
- **ZIP hash:** checked before extraction. The SHA-256 was `54BD140153AAB992A6DE2A5E7F0F597390B26E0E331DEB6970CAD4388D5C175E`, which equals the approved R03 checksum.
- **Command:** only the documented one, `powershell -NoProfile -ExecutionPolicy Bypass -File .\run-proof.ps1`.

## Result: OVERALL FAIL
| Item | Result |
|---|---|
| Manifest verification and template lint | Passed: engine, font, template, common-template and input hashes all equal the approved values |
| Engine version | Empty, because the engine did not start |
| All 5 negative controls | `exitCode = -1073741515`, `errorClass = other_error`, `refusalCause = null`, package folder still empty |
| All 4 templates, both runs | No PDF produced; `runsIdentical = false`, `matchesExpected = false` |

The returned evidence file is `results-PacificStandardTime.json`. It is reproduced in the appendix below as returned.

## Diagnosis
- **Exit code -1073741515 is `0xC0000135`, `STATUS_DLL_NOT_FOUND`.** Windows could not load a DLL that `typst.exe` depends on, so the process ended before Typst ran.
- **The engine fails on its own, without the kit.** The owner ran `.\engine\typst.exe --version` in the extracted folder; it printed nothing and exited with `-1073741515`.
  - This was a read-only diagnostic outside the approved command. It is disclosed here, and it did not change the VM.
- **Windows did not block the program.** `Get-MpComputerStatus` reported `SmartAppControlState = Off`.
- **The binary is the approved one,** because its hash equals the reviewed engine hash.
- **The likely missing dependency is the Microsoft Visual C++ runtime** (for example `vcruntime140.dll`).
  - This is an **unconfirmed** inference. The official Typst Windows build is an MSVC build.
  - The author machine already has the runtime from other software; a clean Windows install does not.
  - The exact import list has not been taken yet. It will be in R04 (see the Recommendation).

## What this run does and does not show
- **It does not show anything about determinism, time-zone independence or isolation.** No PDF was rendered on the second machine.
- **The five controls are NOT evidence of refusal.** They meet the script's "refused" rule (no PDF and a non-zero exit) only because the engine never started. They must not be counted as passes.
  - The kit's refusal rule accepted a failure to start. R04 should require the engine to start successfully (a `--version` preflight) before any control is evaluated.
- **It exposes a real deployment risk.** Typst would fail the same way on a clean clinic PC unless the product ships or installs the runtime. The proposal must cover this whichever fix is chosen here.

## Recommendation: proof kit R04, with the runtime packaged in the kit (option 1)
1. **Confirm the dependency list.** List the DLL imports of the approved `typst.exe` on the author machine. Record every DLL that is not part of Windows itself.
2. **Add those runtime DLLs next to the engine.**
   - They must be the redistributable files from Microsoft's official Visual C++ Redistributable, in their redistributable form, with Microsoft's redistribution terms in `notices\`.
   - Pin each one in `manifest.json` by its SHA-256, and record its version.
   - `typst.exe` itself is unchanged and keeps its approved hash.
3. **Add a startup preflight.**
   - The runner runs `typst --version` first and records the version.
   - If the engine does not start, the run ends with `ENGINE_START_FAILED`. No control or render is evaluated.
   - Each negative control also has to fail with a Typst error message, not just any non-zero exit.
4. **Change nothing else.** Templates, inputs, fonts, expected outputs and every other control stay byte-for-byte the same.
5. **Re-run the author machine,** then rebuild R04 with a new ZIP SHA-256 for a closure review.
6. **After approval,** run it again on the **same clean VM**, unmodified. The VC++ Redistributable must not be installed there, so the run proves the kit is self-contained.

### Options not recommended
- **Install the VC++ Redistributable on the VM and rerun R03.** It is simpler, but it changes the test machine instead of the kit, and it would not show what a clean clinic PC needs.
- **A Typst build with no external runtime dependency.** As far as the author knows, no official build like this exists. An unofficial build would replace the reviewed binary.

## Decision requested from the reviewer
1. Accept this run as FAIL (environment: runtime dependency missing), with no conclusions drawn on determinism or isolation.
2. Approve the R04 approach above, or direct an alternative.

No implementation is requested or authorized.

## Appendix: returned evidence (`results-PacificStandardTime.json`, summarised field-for-field)
- `kit`: "ALV-N026 Typst proof kit R03"
- `engine.version`: ""
- `engine.sha256`: `081217a463adb006f8894b44227fe4b9c9e91fc85f5463d5948e8370db9bb31e`
- `fonts`:
  - Regular `478c558ea716033cd60c03438f628dfa75694dcf6b5f6d505a2f05fd2b4f3823`
  - Bold `1df075a380fc7cb898acf64c1f7b3b4dd780de3caa860178bf929de35817a913`
- `issueTimestamp`: 1795089600 (2026-11-19T12:00:00Z)
- `system`: Windows NT 10.0.26200.0; PowerShell 5.1.26100.7920; Pacific Standard Time; offset -08:00:00
- `controlledConfiguration`: as specified in kit R02/R03; the template lint passed; timeout 30000 ms
- `negativeControls` (all 5): `refused=true`, `exitCode=-1073741515`, `errorClass=other_error`, `refusalCause=null`, `packageFolderEntriesAfter=0`
- `results`. Each entry gives the template's hash, the input's hash and the expected output. The common template hash is `d8aadede336ca66c9a02afc70987e3e59c646185ffe85feaec2be692ebeb8f06` for all four. For every template, both runs' hash and size are `null`, and `runsIdentical` and `matchesExpected` are both `false`.

  | Template | Template hash | Input hash | Expected output |
  |---|---|---|---|
  | prescription-p2 | `7888fa6a…9793` | `ecd92c21…050c` | `b9c68508…d007`, 29,368 bytes |
  | treatment-plan-copy-v1 | `9a6a8aff…b0e` | `a8668902…148c` | `07689e54…d3c3`, 27,445 bytes |
  | statement-v4 | `eafa2eeb…347f` | `6f77a7d7…7c38` | `7db456de…4354`, 27,944 bytes |
  | receipt-v4 | `a18dba12…dae` | `01c5a942…259` | `cdf31259…ad42`, 24,645 bytes |
- `overall`: "FAIL"
