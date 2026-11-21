# ALV-N026 Typst proof kit R02: response to the kit R01 review (CHANGES_REQUIRED)

**Date:** 2026-11-20. **Status:** AWAITING_REVIEW. The kit has **not** been distributed or run on a second machine. It lives outside the repository, and nothing was added to DentalKeep.

**Artifact:** `ALV-N026_typst-proof-kit-R02.zip`

**ZIP SHA-256 (the separately stated checksum for the operator):**
```text
1A97A4D2E748F3F7F4A0976C313C0F378E1A4DABDAB98772A86B3109CD3CAB56
```

| Kit R01 item | Change in R02 |
|---|---|
| 1. Authenticate before extraction | README step 1: compute the ZIP's SHA-256 with `Get-FileHash` **before extracting** and compare it with the checksum above, given through this review record (not the kit). Any difference: stop, extract nothing, report. The in-kit `manifest.json` (18 files) remains a second layer only |
| 2. Real no-network negative control | An in-kit control renders an `@preview/cetz:0.3.4` import under the controlled configuration. It must be refused with no PDF. The result JSON records the refusal, the cause and the configuration. The template-import lint now runs in the kit (any `#import "@` refuses the run) and stays a permanent requirement for the later implementation proposal |
| 3. File-access boundary | In-kit controls create a deliberately unapproved file in a separate temporary folder, then try to read it by absolute path, by relative escape and as JSON input, and try to read a bundled font file as data. Each must be refused with no PDF. Only outcomes and error classes are recorded; no paths or contents |
| 4. Expanded result record | Records the effective values: engine version and hash; each font, template, common-template and input hash; expected and actual PDF hashes and sizes for both runs; the fixed issue timestamp; Windows and PowerShell versions, time-zone id and current UTC offset; the controlled root, package, cache, font path, proxies, removed variables and timeout (as `<work>` and `<kit>` placeholders); the lint result and every control outcome |
| 5. Script comment | The header comment was rewritten; the literal location `%TEMP%\typst-proof-results` is stated. No tab characters remain in the script or the README |

## Author-machine run of the R02 kit
Central Standard Time, UTC-06:00, Windows 10.0.26300, PowerShell 5.1: **OVERALL: PASS.**

| Negative control | Refused | Error class | Cause |
|---|---|---|---|
| external `@preview` package import | yes | package_unavailable | see the disclosure below |
| unapproved file, absolute path | yes | absolute_path_refused | Typst refused a path outside `--root` |
| unapproved file, relative escape | yes | path_escapes_root | Typst refused a path outside `--root` |
| bundled font file read as data | yes | absolute_path_refused | Typst refused a path outside `--root` |
| unapproved file as JSON input | yes | absolute_path_refused | Typst refused a path outside `--root` |

The determinism results are unchanged from proposal R02: each template was byte-identical across both runs and matched the expected hashes (prescription-p2 `b9c68508…d007`, 29,368 bytes; treatment-plan-copy-v1 `07689e54…d3c3`, 27,445; statement-v4 `7db456de…4354`, 27,944; receipt-v4 `cdf31259…ad42`, 24,645).

## Disclosure: what actually refuses the package import
The empty package and cache folders do **not** stop the attempt on their own. Typst tried to download `https://packages.typst.org/preview/cetz-0.3.4.tar.gz`; the connection was **refused at the closed proxy address** (`127.0.0.1:9`, os error 10061), and nothing was written to the package folder.

So, for templates, the real guarantees are:
1. **the import lint plus hash-pinned, server-chosen templates**, which mean no product template can contain an import;
2. the closed proxy, which makes any attempt fail closed (about 2 s, well inside the 30 s timeout).

The kit does **not** prove that Typst could never open a direct connection that ignores the proxy variables. That would need socket-level monitoring, which is outside this kit. The firewall rule stays defence in depth, as the review requires. If the reviewer needs a socket-level proof, it can be added as a separate scratch check (for example outbound-connection monitoring during the controls); it was not done here.

## Run conditions (unchanged)
- No second-machine run until this R02 kit is approved.
- After approval, the operator:
  1. verifies the ZIP checksum before extraction;
  2. runs only `powershell -NoProfile -ExecutionPolicy Bypass -File .\run-proof.ps1` on a PC set to a time zone other than Central;
  3. returns `%TEMP%\typst-proof-results\results-<TimeZoneId>.json`.
