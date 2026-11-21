# ALV-N026 PDF library comparison (for review; no dependency added)

**Date:** 2026-11-20. **Status:** draft for owner review. No product dependency, source change or further spike until this is reviewed (owner, 2026-11-20).

## Why this exists
I first proposed PDFsharp/MigraDoc, believing it was MIT. That premise was wrong. The scratch spike (outside the repository) found that **PDFsharp-MigraDoc 6.5.0** declares `AGPL-3.0-only` with `requireLicenseAcceptance` in its own NuGet metadata. Its first `PdfDocument` constructor threw `PdfSharpLicenseException: The loaded license is not valid`, so no PDF was produced and no bytes were compared. The owner ruled out falling back to the last MIT release (6.2.4): it is no longer maintained.

## Requirements every option is judged against (from the F-108/F-109 decision)
- **Server-side and offline:** no external service and no patient data leaving the machine.
- **Licence:** commercially usable in a closed-source product, with the exact current licence stated.
- **Maintained:** an active release line.
- **Deterministic:** the same input gives byte-identical output, so historical versions can be rebuilt byte-for-byte.
- **Deployment footprint:** does it need a runtime, a browser, a native binary, a commercial licence or a licence key?

## Options

| Option | Current licence (as found) | Commercially usable closed-source without payment? | Maintained | Runtime / footprint | Licence key? | Determinism (what is known) |
|---|---|---|---|---|---|---|
| **PDFsharp / MigraDoc 6.5+** | AGPL-3.0 or paid commercial, from 6.5.0 (official licence page; 6.5.0 released 2026-10-14). The commercial price is cited by third parties as about EUR 750, unverified | No: AGPL, or buy | Yes | Managed .NET only | Yes: a runtime check that requires `SetAgplLicense()` or a loaded commercial key (spike-confirmed) | Fixable metadata (dates, trailer `/ID`) per the API; **unverified**, because the spike stopped at the licence check |
| **QuestPDF** | Community Licence v3.0 (in force 2026-07-06), source-available and not OSI. Free under USD 1M annual gross revenue; never free for the public sector or listed companies; otherwise paid | Only while the practice or vendor stays under the threshold, with a 90-day transition after crossing it | Yes | Managed .NET plus a bundled native Skia | Licence-type configuration in code (`QuestPDF.Settings.License`) | Not verified; Skia-based (see the SkiaSharp row) |
| **iText Core 9** | AGPL-3.0 or commercial (custom price). AGPL use also requires keeping the producer line | No | Yes | Managed .NET | Commercial key for the commercial build | Not verified |
| **Syncfusion PDF** | Commercial; the Community Licence requires under USD 1M revenue, 5 or fewer developers and 10 or fewer employees, validated by ticket | Only if eligible; government use is unclear | Yes | Managed .NET | Yes: a registered licence key | Not verified |
| **SkiaSharp `SKDocument.CreatePdf`** (direct drawing, no layout engine) | MIT (SkiaSharp) and BSD-3 (Skia). **The licence is not re-confirmed in this search**; check the package before relying on it | Yes | Yes (Microsoft-maintained) | Managed plus a native `libSkiaSharp` per platform | No | The creation date can be left unset, but Skia's metadata code appears to hash the current time into the XMP document UUID. That is a likely nondeterministic field, unverified, and needs a spike. We would write our own layout (text wrapping, tables) |
| **Typst CLI** (template language, invoked as a process) | Apache-2.0 | Yes | Yes (active) | A single native executable shipped with the app; templates and bundled fonts are files | No | Official CLI: the creation date is settable through `--creation-timestamp` or `SOURCE_DATE_EPOCH`. Byte-identical output with bundled fonts and `--ignore-system-fonts` is **reported by users, not officially guaranteed**; needs a spike. Pinning the binary version matters, because a different Typst version may lay out differently |
| **Headless Chromium** (PuppeteerSharp or Playwright .NET, rendering HTML) | MIT / Apache-2.0 wrappers; Chromium is BSD-style | Yes | Yes | **A full browser runtime** (about 150 to 300 MB) on the server; process management | No | Poor: Chromium stamps a creation date and uses randomised font-subset tags and IDs, so byte-for-byte rebuild is not realistic without post-processing. Not recommended for this requirement |
| **A minimal in-house PDF writer** | Ours | Yes | Ours to maintain | None | No | Fully deterministic by construction. Cost: fonts (embedding and subsetting, or standard-14 fonts only with WinAnsi text), layout and wrapping, and maintenance. Standard-14 fonts would limit non-Latin names |

Corrected from my earlier list: **PdfPig** is primarily a PDF reading and extraction library, not a document-generation solution, so it is not a candidate.

## Initial read (not a decision)
- **Excluded on licence:**
  - PDFsharp 6.5+ and iText: AGPL, or a paid licence plus a runtime key.
  - QuestPDF and Syncfusion: revenue- or size-conditioned. This is a cost-model and business decision for the owner, and must be escalated if pursued.
- **Excluded on determinism:** headless Chromium.
- **Candidates for a second scratch spike (needs the owner's go):**
  1. **Typst CLI**, pinned version, bundled fonts, `--ignore-system-fonts`, fixed creation timestamp. Byte-compare two renders, then the same render on a second run day. It adds a native executable to the deployment, which is an escalation item for the release package.
  2. **SkiaSharp direct drawing:** check whether the XMP UUID can be fixed or suppressed. If it cannot, the option fails the byte-for-byte requirement.
  3. **A minimal in-house writer** as the fallback if both fail. It needs no dependency, but costs more build and maintenance.
- **Historical rebuild, whichever option is chosen:** pin the exact engine version and fonts per template version, and store them with the template, so an engine upgrade never silently changes an old document's bytes.

## Sources
- PDFsharp licence page: https://docs.pdfsharp.net/General/License/License.html
- PDFsharp 6.5.0 release discussion: https://github.com/empira/PDFsharp/discussions/413
- The 6.5 runtime check, reported downstream: https://github.com/Tharga/Reporter/issues/27 and https://github.com/anton-makarevich/MakaMek/issues/1581
- QuestPDF Community Licence: https://www.questpdf.com/license/community.html and pricing: https://questpdf.com/pricing.html
- iText licence models: https://itextpdf.com/how-buy and the AGPL page: https://itextpdf.com/AGPL
- Syncfusion Community Licence: https://www.syncfusion.com/products/communitylicense
- Typst (Apache-2.0): https://github.com/typst/typst; the creation timestamp and `SOURCE_DATE_EPOCH` in the CLI man page: https://man.archlinux.org/man/typst-compile.1.en; a user report of reproducible output: https://forum.typst.app/t/imprint-branded-pdfs-from-markdown-with-typst-as-the-engine/9156
- Skia PDF metadata (UUID from the current time): https://skia.googlesource.com/skia/+/main/src/pdf/SkPDFMetadata.cpp; the `SKDocumentPdfMetadata` API: https://learn.microsoft.com/dotnet/api/skiasharp.skdocumentpdfmetadata

Third-party vendor articles (IronSoftware) were seen in the results but not relied on, because they come from a competitor.

## Measured spike results (2026-11-20, scratch only, outside the repository)
Method: the same prescription layout (practice, patient, MRN, address, medication table, signature line, a rotated CANCELLED watermark) rendered twice by **separate processes** about 2 s apart. Fonts: Arial regular and bold copied into a spike font folder. Outputs were byte-compared with `cmp`.

**Typst 0.15.1** (official Windows x86_64 release zip, SHA-256 `19ce3551153c2fe7ee9fa2f95208310c8f4d3209fedb699e0333faf8913f6736`; the shipped LICENSE is Apache-2.0 and the NOTICE lists MIT third-party parts). Flags: `--ignore-system-fonts --ignore-embedded-fonts --font-path fonts`, data passed as JSON through `--input`, each run in its own copied folder.

| Mode | Result | Varying fields |
|---|---|---|
| No timestamp | DIFFER (46 bytes) | `CreationDate`/`ModDate`, XMP dates and `xmpMM:InstanceID`, and the second trailer `/ID` (all derived from the wall clock) |
| `--creation-timestamp` fixed | **IDENTICAL** (50,410 bytes) | none. Dates are written in UTC (`D:20261119120000Z`); the InstanceID and `/ID` follow the timestamp; font subset tags are stable |
| Fixed plus `--pdf-standard a-2b` | **IDENTICAL** (53,663 bytes) | none |

Not exercised: a different time zone (Windows ignored the `TZ` variable in the default run). The fixed mode writes UTC, so the time zone should not matter, but this is unproven on a second machine.

**SkiaSharp 4.157.0** (SkiaSharp and NativeAssets.Win32 nuspecs: MIT). Direct drawing through `SKDocument.CreatePdf`.

| Mode | Result | Notes |
|---|---|---|
| No dates | IDENTICAL (1,110,913 bytes) | no `/ID` and no XMP are written |
| Fixed `Creation`/`Modified` | IDENTICAL | **Defect: a 12:00 UTC value is written as `D:20261119120000-06'00'`**, the UTC clock time labelled with the machine's local offset. The bytes depend on the host's time zone, and the date is wrong |
| PDF/A (`PdfA = true`) with fixed dates | **DIFFER (104 bytes)** | trailer `/ID` and XMP `uuid:` change on every run (time-derived), as the source suggested |

SkiaSharp also embedded about 1.1 MB of font data for two fonts, against Typst's 50 KB for the same page. There is no layout engine: the text positions were hand-coded.

**Read:** Typst met the byte-for-byte requirement in every fixed mode tested. SkiaSharp meets it only without PDF/A and without dates (or with a work-around for the offset defect), and needs our own layout code. Neither is added to DentalKeep; this waits for review.
