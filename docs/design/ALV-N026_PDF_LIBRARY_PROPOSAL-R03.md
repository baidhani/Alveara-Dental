# ALV-N026 PDF library proposal (R03): proven determinism, the Visual C++ runtime prerequisite, open approval items

**Story:** ALV-N026 (item 63, PKG-12). **Date:** 2026-11-21. **Status:** AWAITING_REVIEW (design only).

**Nothing has been added to DentalKeep:** no Typst, no runtime, no templates, no fonts, no PDF code, no installer change, no migration and no configuration. Every artifact referred to below lives in scratch or in `docs/design/`.

**Decision requested:**
- Approve **Typst 0.15.1** with **Noto Sans 2.015** and **PDF/A-2b** as the server-side PDF engine for newly issued documents.
- Approve it with its **Microsoft Visual C++ runtime prerequisite delivered by the official Microsoft Visual C++ Redistributable, installed and serviced centrally** (sections 4 and 8).
- Approve it under the controls in sections 1 to 8, subject to the open items in section 9.

**What changed since R02:**

| Area | R02 | R03 |
|---|---|---|
| Second-machine determinism proof | Pending | **Done and accepted.** Run 2 on a clean Pacific-time VM: PASS; review APPROVED (section 4) |
| Runtime prerequisite | Not identified; Typst was described as a single self-contained executable | **Identified and evidenced.** Typst needs `VCRUNTIME140.dll`. Production gets it from the installer-managed Microsoft redistributable (section 8, new) |
| Release store | Typst ZIP and fonts | Also the official `vc_redist.x64.exe` with its hash, signature and provenance (section 7) |
| Open items | Implicit | Listed (section 9) |

Sections 1, 2, 3, 5 and 6 are unchanged in substance from R02. They are restated here so that this revision stands alone.

**Owner decisions recorded:**

| Date | Decision |
|---|---|
| 2026-11-20 | Item 7, store: an owner-controlled versioned folder |
| 2026-11-20 | Item 4: a self-contained proof kit run on another Windows PC in another time zone |
| 2026-11-20 | Item 5: legacy reprints are labelled new PDFs, built from hash-verified record content |
| **2026-11-21** | **Production runtime: the installer-managed official Microsoft Visual C++ Redistributable.** App-local deployment was used only for the proof kit |
| 2026-11-21 | The release owner affirmed their entitlement to redistribute Microsoft's Visual Studio 2022 Distributable Code as a licensed Visual Studio 2022 (Community) user |

## 1. Network isolation (unchanged)
**Applied to every render:**
1. Templates are approved, hash-pinned files, never supplied by the caller. An import-lint test rejects `#import "@…"`.
2. `TYPST_PACKAGE_PATH` and `TYPST_PACKAGE_CACHE_PATH` point at an empty folder for each render.
3. Every proxy variable, upper and lower case, is set to the closed address `http://127.0.0.1:9`.
4. These are removed from the environment: `TYPST_FONT_PATHS`, `TYPST_ROOT`, `TYPST_IGNORE_*`, `SOURCE_DATE_EPOCH` and `NO_PROXY`.
5. A 30-second timeout, followed by kill.

**Evidence:**
- **Listener proof (R02):** the positive control was caught, and no product template made a connection.
- **Kit controls on both machines:** the `@preview` import was refused (`package_unavailable`). The download was attempted and refused at the closed proxy, and nothing was written to the package folders.

**Limit (unchanged, accepted by the reviewer):** there is no socket-level proof that Typst can never connect directly. A Windows Firewall outbound block for `typst.exe` in the installer (ALV-N013) is **defence in depth**.

## 2. File-access boundary (unchanged)
**For each render:**
- a fresh, ACL-restricted folder `%TEMP%\dk-pdf\<guid>`;
- only the hash-checked template files and `input.json` are copied in;
- `typst compile --root <folder>`;
- fonts are supplied only through `--font-path`;
- the output is read back and the folder is deleted in `finally`.

**What the server and caller control:** callers supply only the document id and the action. The server chooses every path and argument, and the process starts with no shell.

**Evidence:**
- R02: seven reads outside the root were refused, and the only file written was `out.pdf`.
- The kit, on both machines: an absolute path, a relative escape, a font read as data and JSON input from outside the root were each refused by Typst (`absolute_path_refused` and `path_escapes_root`).

**Limit:** OS-level confinement (a low-privilege service account) is an ALV-N013 installer design item.

## 3. No new snapshot store (unchanged)
The PDF is a pure function of three inputs:
- the template version;
- the canonical content, regenerated from the **existing** controlled records;
- the stored issue timestamp.

No copy of clinical or financial content is added. Each issued PDF gets an **issuance record** holding:
- the document id;
- the template version;
- the engine, font and runtime bundle id;
- the issue timestamp;
- the canonical-content SHA-256;
- the PDF SHA-256.

**How existing rules carry over:**

| Topic | Rule |
|---|---|
| Permissions | The same as viewing the material today |
| Audit | Issue, reprint and rebuild events go through the existing patient-access print events (ALV-N025) |
| Retention, backup and export | Follow the source records |
| Integrity | Re-render only when the regenerated content hashes to the stored value; otherwise `integrity_mismatch` |
| Patient merge | Per the reviewer's stop rule, any merge-scope change is reviewed first |

**Open (section 9):** prescription status-as-of-print for rebuilds.

## 4. Determinism gate: PASSED for the proof scope
**The kit:** the final approved kit is R05 (ZIP SHA-256 `E39D7238…D6AA`). It contains:
- the pinned `typst.exe` (`081217a4…b31e`);
- an app-local `vcruntime140.dll` 14.44.35211.0 (`d5e4d9a3…9066`), used for the proof only;
- the Noto Sans 2.015 fonts;
- four representative templates and synthetic inputs.

**The runner:**
- verifies the manifest;
- requires the exact engine version before anything else (`ENGINE_START_FAILED` otherwise);
- runs five negative controls, each of which counts only as a refusal by a running Typst;
- renders each template twice under the section 1 and 2 configuration with the fixed timestamp `1795089600`;
- compares the outputs with each other and with the pinned expected hashes.

| Run | Machine | Result | Review |
|---|---|---|---|
| Author | Central Standard Time (UTC-06:00), Windows 10.0.26300 | PASS (R03 to R05 kits) | — |
| VM run 1 | Clean, separately installed VM, Pacific (UTC-08:00), Windows 10.0.26200, network off | **FAIL**: `typst.exe` exit `0xC0000135`, missing `VCRUNTIME140.dll`. Nothing rendered | Accepted as FAIL; led to R04 and R05 (`ALV-N026_TYPST_PROOF_SECOND_MACHINE_RUN1_REVIEW.md`) |
| **VM run 2** | The same VM, unmodified, network off | **PASS**: exact-version preflight; 5/5 controls refused with the expected classes; 4/4 templates identical within the machine **and** identical to the author machine | **APPROVED** as evidence (`ALV-N026_TYPST_PROOF_SECOND_MACHINE_RUN2_REVIEW.md`) |

| Template | Bytes | PDF SHA-256 (identical on both machines) |
|---|---|---|
| prescription-p2 | 29,368 | `b9c68508cda5c7e61702c4e62d62e19467611729d1430238c1ce73532019d007` |
| treatment-plan-copy-v1 | 27,445 | `07689e545dab6e46e1cc426ba9588e2f1158a56445102cd592d093baeeb1d3c3` |
| statement-v4 | 27,944 | `7db456de7bb33365b1257674f76d3945889d8136b2844bdf94a983d91a414354` |
| receipt-v4 | 24,645 | `cdf31259a8b915a5d37d5c106431b84c0d72bfa645ef5d48949b29dca061ad42` |

**Scope of the claim:** the output is byte-identical for this engine, runtime, font set, these templates and a fixed timestamp, across two Windows machines in two time zones. Other Windows builds, locales and daylight-saving dates were not covered.

**The gate repeats at implementation:**
- with the real product templates;
- on a clean machine set up by the **installer-managed** runtime (section 8), not the app-local proof copy;
- before closure.

A mismatch stops the story for redesign. The byte-for-byte requirement is not weakened without a new owner and reviewer decision.

## 5. Legacy documents (unchanged; owner decision 2026-11-20)
- **No legacy printout is promised byte for byte.** `p1` (the prescription canonical-content version) and financial layouts `v1` to `v3` were drawn in the browser, and no rendered printout was stored.
- **A reprint of a pre-PDF record** regenerates the canonical content and **verifies its retained hash first**. On success it produces a new PDF labelled: *"Reprint generated <date> from the verified record. This is not the original browser printout."* On failure it refuses and reports an integrity issue.
- **`p1` prescriptions have no stored hash.** How they are verified, or whether they are refused, is open (section 9).
- **Byte-for-byte reconstruction** applies only to documents first issued as controlled PDFs: prescription p2, treatment-plan copy v1, and statements and receipts from v4 onward.

## 6. Archival format and fonts (unchanged)
- **PDF/A-2b** (`--pdf-standard a-2b`). It was byte-identical in every run on both machines.
- **Noto Sans 2.015**, hinted TTF, from the official Noto release:
  - release zip `0c34df07…58f5`;
  - Regular `478c558e…3823`;
  - Bold `1df075a3…a913`;
  - SIL OFL 1.1, shipped and listed in the third-party notices.
- `--ignore-system-fonts --ignore-embedded-fonts`.
- Each template version records its exact font set, so a font change means a new template version.

## 7. Release acquisition and provenance (owner decision 2026-11-20, extended)
**The owner-controlled, versioned store.** Folders are created only after this proposal is approved:

```
C:\.alvera-data\release-artifacts\
  typst\0.15.1\windows-x64\
    typst-x86_64-pc-windows-msvc.zip   (upstream ZIP, SHA-256 19ce3551...f6736)
    SHA256SUMS.txt                     (ZIP + extracted typst.exe 081217a4...b31e)
    LICENSE, NOTICE, SOURCE.txt, APPROVAL.md
  fonts\noto-sans\2.015\
    NotoSans-Regular.ttf, NotoSans-Bold.ttf, OFL.txt, SHA256SUMS.txt, SOURCE.txt, APPROVAL.md
  msvc-runtime\14.44.35211.0\x64\                       (NEW)
    vc_redist.x64.exe                  (official Microsoft package, SHA-256 cc0ff0eb...b713b, Authenticode Valid)
    SHA256SUMS.txt
    PROVENANCE.txt                     (source, set 14.44.35112 vs file 14.44.35211.0 reconciliation,
                                        redistribution-list entry, owner entitlement, as in the R05 kit record)
    APPROVAL.md
```

**Rules (unchanged, now covering the runtime package too):**
- The release script only copies from the store. It refuses a missing file or a hash mismatch, and it also refuses a `vc_redist.x64.exe` whose Authenticode signature is not Valid with a Microsoft signer.
- The product build never downloads anything.
- An approved version folder is never overwritten; a new version gets a new folder.
- A private GitHub Release may replace the folder later (owner).

## 8. Visual C++ runtime prerequisite (NEW)
**Evidence (kit R05 and VM runs 1 and 2):**
- `typst.exe` imports one non-Windows DLL, `VCRUNTIME140.dll`. Its other 16 imports are Windows DLLs or the Universal C Runtime, which is part of Windows 10 and later.
- `vcruntime140.dll` itself needs only the Universal C Runtime and KERNEL32.
- Without the DLL, Typst does not start (`0xC0000135`).
- With it, Typst passes the full gate on a clean machine.

**Production design: installer-managed, central deployment.** Microsoft recommends this because Microsoft can then service the runtime.

1. **Install.** The DentalKeep installer (ALV-N013 scope) runs the release-store `vc_redist.x64.exe` with `/install /quiet /norestart`, after re-verifying its hash and signature.
   - Exit codes `0`, `1638` (Windows Installer: another version of the product, in practice a newer one, is already installed) and `3010` (installed, restart needed) count as success. A `3010` result is reported to the operator.
   - **Any other code fails the installation with a clear message,** so there is never a silent partial install.
   - Re-running the installer is safe: the redistributable is idempotent, and `1638` is success.
2. **No app-local copy in the product.** `vcruntime140.dll` is **not** shipped next to `typst.exe` in DentalKeep. The proof-kit copy was for evidence only.
3. **Readiness check, at API startup and before each render batch.** This is the R04/R05 preflight carried into the product:
   - verify the `typst.exe` hash;
   - run `typst --version` with a timeout;
   - require exit 0 and exactly `typst 0.15.1 (9dfd3a08)`.
4. **Failure behaviour.** If readiness fails (missing runtime, start refused, wrong version or timeout):
   - PDF issuing is **unavailable**, with a stable error: `pdf_engine_unavailable` (503) for start or runtime failures, and `pdf_engine_untrusted` for a hash mismatch, as in R02;
   - there is **no fallback engine** and no browser-print substitute for controlled documents;
   - it is logged with `error_class` and redacted details;
   - the rest of the application, including offline clinical and administrative work, is unaffected;
   - the admin health view shows the state and the remedy: re-run the installer or repair the Microsoft Visual C++ Redistributable.
5. **Servicing.** Microsoft Update services the central runtime. A runtime update does not change the reviewed Typst binary or fonts.
   - **Unproven:** that PDF bytes are identical across runtime **versions**. The determinism evidence covers runtime 14.44.35211.0 only.
   - **Control:** the golden-file gate (section 4) runs at each DentalKeep release on a clean installer image. The readiness check also records the installed runtime version in each issuance record's bundle id, so any byte change after a runtime update is detectable and attributable.
   - If a later runtime changes output, it is handled as a template-version or engine event under the section 4 stop rule, never silently.
6. **Licence.** Microsoft's Visual Studio 2022 Distributable Code ("Visual C++ Runtime Files"), for licensed Visual Studio users. The release owner's entitlement was affirmed on 2026-11-21. It must still hold for each release that ships the package, and it is recorded in `APPROVAL.md` for the runtime folder.

## 9. Open approval items (retained)
These must be settled before or within the pre-implementation brief. None is decided by this proposal.

1. **Pre-implementation brief for ALV-N026.** The `ManageDocuments` permission and the PDF dependency both escalate under the governance rules and need explicit approval in the brief.
2. **Prescription rebuilds:** whether the status-change record gives the status as of a given print; otherwise only verifiable prints are rebuilt.
3. **`p1` prescription reprints:** how content is verified without a stored hash, or a refusal.
4. **PD-11 research note:** the exact US sources, fields and exclusions for the jurisdiction scope.
5. **Installer work (ALV-N013 scope):**
   - the redistributable step and failure handling in section 8;
   - the firewall rule in section 1;
   - the service-account ACLs in section 2.

   Changes to the installer are not authorized by this proposal.
6. **Release-store folders and `APPROVAL.md` files:** created only after this proposal is approved.
7. **Product-wide:** the determinism gate at implementation on a clean installer-managed image (section 4), and the runtime-version disclosure in section 8.5.

## Unchanged from R01 and R02
- **Failure handling:**
  - a 30-second timeout and kill;
  - one retry, for a crash or timeout only;
  - a concurrency cap of 2;
  - stable error classes and redacted stderr;
  - idempotent renders.
- **Required tests:**
  - import lint;
  - hash-mismatch refusal;
  - `integrity_mismatch` refusal;
  - the legacy-reprint label;
  - golden files for each template version.
- **Added by R03:**
  - the readiness-check refusal (`pdf_engine_unavailable`) with the engine not startable;
  - the installer redistributable exit-code handling (in ALV-N013's test plan).
