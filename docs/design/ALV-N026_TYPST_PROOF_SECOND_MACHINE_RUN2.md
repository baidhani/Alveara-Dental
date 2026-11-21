# ALV-N026 Typst proof kit R05: second-machine run 2 (PASS)

**Date:** 2026-11-21. **Status:** AWAITING_REVIEW of the returned evidence, as the R05 review requires. No implementation is requested.

## Run conditions
- **Machine:** the same clean, separately installed Windows VM as run 1.
  - Microsoft Windows NT 10.0.26200.0, PowerShell 5.1.26100.7920.
  - Pacific Standard Time, UTC-08:00.
- **Nothing installed or changed since run 1.** The Visual C++ Redistributable is **not** installed.
- **Network:** disabled before extraction and during the run (the operator's statement, confirmed 2026-11-21). The operator also supplied a screenshot of the VM taskbar at 7:00 AM on 2026-11-21 (VM local time). It shows the Windows network indicator as disconnected (a globe with a "no connection" mark). The screenshot shows the state at that moment only; the "before extraction and during the run" timing rests on the operator's statement.
- **ZIP hash:** checked before extraction. The SHA-256 was `E39D72388E5A276BAC6E84793CCEA4F96F3F6C4B6364CBDA7E776CE48818D6AA`, which equals the approved R05 checksum.
- **Command:** only `powershell -NoProfile -ExecutionPolicy Bypass -File .\run-proof.ps1`. No other diagnostic was run.

## Returned evidence
- **Console:** `OVERALL: PASS (report: %TEMP%\typst-proof-results\results-PacificStandardTime.json)`. It was preceded by `preflight: exit=0 version='typst 0.15.1 (9dfd3a08)' ok=True`, five `refused=True` control lines with the expected classes, and four template lines with `runsIdentical=True matchesExpected=True`.
- **Result JSON:** `docs/design/evidence/ALV-N026-typst-kit-R05/vm-run2-results-PacificStandardTime.json`, stored exactly as returned.

## Author check of the JSON against the approved kit
| Item | Returned | Approved or expected | Match |
|---|---|---|---|
| Kit | ALV-N026 Typst proof kit R05 | R05 | Yes |
| Engine hash | `081217a4…b31e` | Approved Typst 0.15.1 binary | Yes |
| Preflight | exit 0, `typst 0.15.1 (9dfd3a08)`, no start error, no timeout, started=true | Exact expected version | Yes |
| Runtime DLL | `engine\vcruntime140.dll` 14.44.35211.0, `d5e4d9a3…9066` | R05 provenance record | Yes |
| Fonts | Regular `478c558e…3823`, Bold `1df075a3…a913` | Pinned Noto Sans 2.015 | Yes |
| Templates, common template, inputs | All four template hashes, common `d8aadede…8f06`, all four input hashes | R05 manifest | Yes |
| Issue timestamp | 1795089600 (2026-11-19T12:00:00Z) | Fixed | Yes |
| Template lint | PASS | PASS | Yes |
| 5 negative controls | All exit 1, actual class = expected class, no PDF, package folder empty | As specified in R04/R05 | Yes |
| prescription-p2 | Both runs `b9c68508…d007`, 29,368 bytes | Expected | Yes |
| treatment-plan-copy-v1 | Both runs `07689e54…d3c3`, 27,445 bytes | Expected | Yes |
| statement-v4 | Both runs `7db456de…4354`, 27,944 bytes | Expected | Yes |
| receipt-v4 | Both runs `cdf31259…ad42`, 24,645 bytes | Expected | Yes |
| Overall | PASS | PASS | Yes |

## What this evidence supports
1. **Cross-machine and cross-time-zone determinism.**
   - The four representative PDFs are byte-identical on two different Windows machines in two time zones (Central, UTC-06:00; Pacific, UTC-08:00).
   - They are also identical across two renders on each machine.
   - Each output matches the hashes produced on the author machine.
2. **The kit is self-sufficient on clean Windows.** The engine started on a machine without the Visual C++ Redistributable, using only the app-local `vcruntime140.dll` shipped in the kit. This is the same VM where run 1 failed with `0xC0000135`.
3. **The controls were refused by a running Typst process,** with no network on the VM:
   - the external package import was refused at the closed proxy address, and nothing was written to the package folders;
   - all four reads outside the render root were refused by Typst's root boundary.

## What it does not show (unchanged disclosures)
- **No socket-level proof.** That Typst can never open a direct connection that ignores the proxy variables is not proven. The firewall rule remains defence in depth. (The VM had no network, so no connection was possible in this run either way.)
- **Only one other machine and one other time zone.** Other Windows builds, regional and locale settings, and daylight-saving dates were not covered. The fixed issue timestamp removes time-of-run input by design.
- **No production deployment decision.** The app-local runtime was used for the proof only. Per the R05 review, the product PDF proposal must choose and document the installer-managed Microsoft Visual C++ Redistributable strategy.

## Decision requested from the reviewer
1. Accept second-machine run 2 as PASS evidence for the Typst determinism and isolation claims stated above.
2. Confirm the next step: correct the PDF-library proposal (R03). It will record:
   - this evidence;
   - the actual deployment model: the pinned Typst executable plus the Microsoft Visual C++ runtime, delivered by the installer-managed redistributable;
   - the remaining open items, which are the release store, the PD-11 research and the pre-implementation brief.

No implementation, dependency, installer change or migration is requested or authorized.
