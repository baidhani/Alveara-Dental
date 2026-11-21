# ALV-N026 PDF library proposal (R01): escalation for independent review

**Story:** ALV-N026 (item 63, PKG-12). **Date:** 2026-11-20. **Status:** AWAITING_REVIEW. No product dependency, source change or migration has been made.

**Decision requested:** approve **Typst 0.15.1** (Apache-2.0), run as a pinned local executable, as DentalKeep's server-side PDF engine for prescriptions, treatment-plan patient copies, statements, receipts and clinical record copies, under the controls in section 4. Or reject it, or approve it with conditions.

**Why this is escalated:**
- the owner's print-method decision (F-108/F-109): "an escalation/design checkpoint before adding the dependency";
- CLAUDE.md: a new external dependency is a strategic decision;
- the release package gains a native executable.

## 1. Owner requirements (decision record, F-108/F-109)
| Requirement | How it is met |
|---|---|
| Server-side PDF; the browser prints or downloads the PDF, not the app page | The API renders the PDF and serves it as `application/pdf`. Because the browser no longer prints the page itself, its headers and footers, including the internal patient URL, cannot appear on the document (F-109), and there is no blank second sheet (F-108) |
| Licence, maintenance, security, offline and determinism stated up front | Sections 2 and 3 |
| No external PDF service and no patient-data upload | A local process, with no network use (section 4, item 5) |
| Versioned templates and a byte-for-byte rebuild of historical versions | Section 4, items 1 to 3 |

## 2. Options considered
Full comparison and sources: `ALV-N026_PDF_LIBRARY_COMPARISON.md`.
- **Excluded:**
  - PDFsharp/MigraDoc 6.5+: AGPL or commercial, with a runtime licence check. The spike confirmed it does not run without one. The last MIT release (6.2.4) is unmaintained, and the owner ruled it out.
  - iText 9: AGPL or commercial.
  - QuestPDF and Syncfusion: free only under revenue or company-size limits; a cost decision not taken.
  - Headless Chromium: a browser runtime, and its output varies from run to run.
  - PdfPig: a PDF reader, not a generator.
- **The fallback was tested and is weaker:**
  - SkiaSharp 4.157.0 (MIT) has no layout engine.
  - It wrote a fixed 12:00 UTC date with the machine's local offset (`-06'00'`), so the bytes depend on the host's time zone.
  - In PDF/A mode its document ID changes on every run.
- **The last resort,** an in-house minimal writer, is not proposed: it is a large build and maintenance cost.

## 3. Measured evidence (spike, outside the repository)
| Item | Value |
|---|---|
| Version | Typst 0.15.1 (`9dfd3a08`), released 2026-07-17 |
| Artifact | `typst-x86_64-pc-windows-msvc.zip`, SHA-256 `19ce3551153c2fe7ee9fa2f95208310c8f4d3209fedb699e0333faf8913f6736` |
| Licence | Apache-2.0 (shipped LICENSE); third-party parts MIT (shipped NOTICE). No licence key, no revenue condition, no runtime check |
| Maintenance | Active upstream project with regular releases (github.com/typst/typst) |
| Determinism, default | Not identical: the dates, the XMP InstanceID and the second `/ID` follow the clock |
| Determinism, fixed `--creation-timestamp` | **Byte-identical** across two separate processes, separate working folders, about 2 s apart (50,410 bytes) |
| Same, with `--pdf-standard a-2b` | **Byte-identical** (53,663 bytes) |
| Dates when fixed | Written in UTC (`D:20261119120000Z`) |
| Font subset tags | Stable across runs |

**Not yet proven (see section 6):**
- identical bytes on a second machine or in a second time zone;
- output on Linux, if a Linux host is ever used.

## 4. Proposed controls
1. **Pinned engine.**
   - Exact version 0.15.1, with the SHA-256 recorded in the repository.
   - The binary is verified against that hash before every use (startup check plus a cached result). A mismatch refuses to render: `pdf_engine_untrusted`, 503. There is no fallback to any other engine.
2. **Versioned templates.**
   - Each document type has immutable template versions (for example `prescription/p2`, `statement/v4`).
   - Each version records the engine version, the template file hash and the font file hashes.
   - Every issued document stores its template version and its input data, a canonical JSON snapshot.
   - A historical document is rebuilt with **its own** template version. Changing a template means a new version; existing versions are never edited, and a unit test fails if a frozen template file changes.
3. **Engine upgrades never silently re-version.**
   - A future Typst upgrade ships alongside the old pinned binary, or is adopted only after golden-file tests prove every frozen version still rebuilds byte for byte.
   - This meets OQ-2's "existing historical documents must continue rebuilding with their original versions byte-for-byte".
4. **Fixed inputs.**
   - `--creation-timestamp` is the document's own stored issue time, never the render time.
   - Flags: `--ignore-system-fonts --ignore-embedded-fonts --font-path <bundled fonts>`.
   - Fonts are licensed for redistribution, for example open fonts under the SIL OFL. **Arial was used only in the spike and will not be shipped.**
5. **Offline and sandboxed process.**
   - The engine runs with the argument list only: no shell, and no user text in arguments.
   - Data is passed as a JSON file in a per-render temporary folder, which is also the `--root`, so templates can read only that folder.
   - Templates must not use Typst `@preview` package imports, which download from the internet. A template lint test rejects any `#import "@`.
   - The package cache and data paths are pointed at an empty folder. **Whether this guarantees no network access in 0.15.1 is to be confirmed during implementation;** if it cannot be confirmed, a firewall rule for the executable is added to the installer.
6. **Failure handling.**
   - Each render has a 30 s timeout, and the process tree is killed on timeout.
   - One retry, only for a crash or timeout, never for a template error.
   - A cap of 2 concurrent renders.
   - Errors carry a stable `error_class` (`pdf_render_timeout`, `pdf_render_failed`, `pdf_engine_untrusted`) and are logged with a correlation ID. The engine's stderr is logged with patient values redacted, because template errors can echo data.
   - The temporary folder is deleted in `finally`.
   - Rendering is read-only and idempotent: the audit print event is recorded once per user action, as today.
7. **Packaging.**
   - `typst.exe` plus its LICENSE and NOTICE ship in the release package and the Windows installer (ALV-N013).
   - Third-party notices are added to the product's licence list.
   - No build-time network download: the binary is committed through the release pipeline's artifact store, or vendored with its hash. That choice is the owner's.

## 5. Tests the implementation must ship
- **Golden files:** each template version renders known input to a committed PDF hash, twice, in separate processes.
- **Frozen versions:**
  - the p1 prescription content and financial template v1 to v3 stay byte-identical, so OQ-2's new versions don't disturb them;
  - editing a frozen template fails a test.
- **Failure paths:** hash mismatch (refused, 503); a timeout from a stub engine that hangs; a template error; temporary-folder cleanup.
- **Content checks:**
  - the PD-11 prescription fields;
  - the F-164 watermark text and the status date and reason;
  - the MRN on all four materials.
- **Browser:** the print action opens the PDF, so no app page is printed.

## 6. Open points for the reviewer
1. **Cross-machine and time-zone determinism is not proven.**
   - Proposed: the golden-file tests in CI and on a second developer machine with a different time zone before closure.
   - The fixed mode writes UTC, so a difference is not expected.
2. **Network isolation of the Typst process** (control 5): confirm by test, or require the firewall rule.
3. **PDF/A-2b:** use it for archival documents? Recommended yes; it was byte-identical in the spike. Reviewer's view requested.
4. **Fonts:** choosing a redistributable font family is part of this approval, for example Liberation Sans (SIL OFL) or Noto Sans (SIL OFL).
