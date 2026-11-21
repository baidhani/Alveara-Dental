# ALV-N026 PDF library proposal (R02): response to the R01 review (CHANGES_REQUIRED)

**Story:** ALV-N026 (item 63, PKG-12). **Date:** 2026-11-20. **Status:** AWAITING_REVIEW. Nothing has been added to DentalKeep: no Typst, templates, fonts, PDF code, installer change, migration or configuration. All evidence below comes from scratch folders outside the repository.

**Decision requested:**
- Approve **Typst 0.15.1** with **Noto Sans 2.015** and **PDF/A-2b** as the server-side PDF engine for newly issued documents, under the controls below.
- That approval is **conditional on the second-machine proof (item 4)**, which is prepared but not yet run.

**Owner decisions taken for this revision (2026-11-20):**
- **Item 7, store:** an owner-controlled versioned folder.
- **Item 4, second machine:** the owner runs a prepared, self-contained kit on another Windows PC in another time zone.
- **Item 5, legacy documents:** a labelled new PDF from hash-verified record content.

| R01 review item | Design change in R02 | Evidence |
|---|---|---|
| 1. Network isolation proven | Section 1 | Section 1: listener proof with a positive control |
| 2. File-access boundary | Section 2 | Section 2: seven read attempts refused; writes observed |
| 3. Render-input snapshot is patient data | Section 3: **no snapshot store**; rebuild from existing controlled records, store hashes only | Code facts cited |
| 4. Determinism gate | Section 4 | Same machine: PASS. Second machine: kit ready, pending |
| 5. Legacy-template statement | Section 5 (owner decision) | Code facts cited |
| 6. PDF/A-2b and Noto Sans | Section 6 | Exact files and hashes |
| 7. Release acquisition route | Section 7 (owner decision) | Folder layout |

## 1. Network isolation
**Primary controls, all applied to every render:**
1. Templates are approved, hash-pinned files, never caller-supplied. A lint test rejects any `#import "@…"` package import, which is Typst's only route to the internet for templates.
2. `TYPST_PACKAGE_PATH` and `TYPST_PACKAGE_CACHE_PATH` point at an empty per-render folder.
3. Every proxy variable (`HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY` and the lower-case forms) is set to the closed local address `http://127.0.0.1:9`, so any proxy-honouring attempt fails closed.
4. These are removed from the child's environment: `TYPST_FONT_PATHS`, `TYPST_ROOT`, `TYPST_IGNORE_*`, `SOURCE_DATE_EPOCH`, `NO_PROXY`.
5. Hard timeout of 30 s, then the process is killed.

**Proof** (`security-proofs.ps1`, scratch): a local TCP listener stood in for the internet, with every proxy variable pointing at it.

| Case | Listener connections | Outcome |
|---|---|---|
| **Positive control:** a template importing `@preview/cetz:0.3.4` | **1** | The download was attempted and caught. The process then hung until the 20 s kill, which shows why the timeout and kill are required |
| Product template `prescription-p2` | 0 | PDF produced |
| `treatment-plan-copy-v1` | 0 | PDF produced |
| `statement-v4` | 0 | PDF produced |
| `receipt-v4` | 0 | PDF produced |

**Limit disclosed:** the listener sees only proxy-honouring HTTP(S). The positive control shows Typst's package downloader honours the proxy variables. That the downloader is Typst's only network code comes from Typst's design (`typst-kit` download module) and was not independently verified at the socket level. A Windows Firewall outbound block for `typst.exe` is documented as **defence in depth only** (installer, ALV-N013).

## 2. File-access boundary
**Per render, the API:**
1. creates a fresh folder `%TEMP%\dk-pdf\<guid>` with restricted ACLs (the service account only);
2. copies in **only** the approved template version's files (hash-checked against the template manifest) and the render's `input.json`;
3. runs `typst compile --root <that folder>`, so template reads are confined to it;
4. passes the approved font folder **only** through `--font-path`, which the font loader reads; templates cannot read it (proved below);
5. writes the output to `<folder>\out.pdf`, reads it back, and deletes the folder in `finally`.

**What callers control:** the document id and the action only. The server chooses the template version, every path and every argument. No patient data appears in arguments; data reaches Typst only through `input.json`. The process starts with an argument list and no shell.

**Proof, reads** (each template tried to read outside its root; each run failed with exit 1 and no PDF):

| Attempt | Typst error |
|---|---|
| `read("../outside.txt")` | path would escape the project root |
| `read("/../../Windows/win.ini")` | path would escape the project root |
| planted secret file, absolute path | failed to load file (invalid component `C:`) |
| `C:/Windows/win.ini` | the same |
| a bundled font file, absolute path | the same; the font folder is not readable by templates |
| `image(...)` outside the root | the same |
| `json(...)` outside the root | the same |

**Proof, writes:** across all renders, the only new file in the render folder was `out.pdf`. Typst's per-user folders (`%LOCALAPPDATA%\typst`, `%APPDATA%\typst`) did not exist before and were not created.

**Limit disclosed:** these are Typst's own boundaries. OS-level confinement, a low-privilege service account with the restricted folder ACLs, is part of the installer design (ALV-N013) and is not proven here.

## 3. No new snapshot store: rebuild from existing controlled records
**What the code does today, read at `318bf3c`:**
- **Statements and receipts** (`FinancialDocument`) store no amounts. They keep:
  - the ledger cut-offs;
  - `TemplateVersion`;
  - the SHA-256 `ContentHash` of the canonical content;
  - a small identity `EnvelopeJson` (practice, patient and responsible party as displayed).

  The document is regenerated from the append-only ledger and checked against `ContentHash`.
- **Prescriptions** build canonical content `p1` from the prescription row at print time. The hash is computed but **not stored**.
- **The treatment-plan patient copy** has no template version. Plan revisions are immutable.

**Design:** the PDF is a pure function of (template version, canonical content, stored issue timestamp). The canonical content is regenerated from the **existing** controlled records, so no copy of clinical or financial content is stored. What is added per issued PDF is a small **issuance record** holding:
- document id;
- template version;
- engine and font bundle id;
- issue timestamp;
- canonical-content SHA-256;
- PDF SHA-256.

It holds no patient data beyond the foreign keys that already exist.

**Handling the reviewer's points:**

| Point | Handling |
|---|---|
| Storage and protection | No new content store. Issuance records sit in the existing database beside their source rows, with the same protection. Rendered PDFs are not stored; they are regenerated on demand and their temporary files deleted |
| Permission | A rebuild or reprint needs the same permission as viewing that material today (for example ViewBilling or ViewPrescriptions plus patient-record access). No new bypass |
| Audit | Issuing, reprinting and rebuilding each record an audit event through the existing patient-access print events (ALV-N025), with the material type and template version |
| Retention, inactivation, backup, export | They follow the source records: append-only, backed up with the database, and exported with the existing domains. A deleted or inactivated source means no rebuild |
| Patient merge | Issuance records reference the source document, not the patient directly. If the brief finds a patient key is needed, the table joins the merge-scope trigger set under the existing rules; per the reviewer's stop rule, any change to merge-scope design is reviewed first |
| Integrity | Before rendering, the regenerated canonical content must hash to the stored value. If it does not, the render is refused with `integrity_mismatch` and logged as an integrity issue. Changed current data is never rendered as if it were the historical document |

**Open point for the brief:** a prescription can change status after a print (for example Active, then Cancelled). Rebuilding a specific earlier print needs the status as of that print, from the prescription's status-change record. The brief will confirm that record holds what is needed. If it does not, prescription rebuilds are limited to verifiable prints, never guessed.

## 4. Determinism gate
**The proof kit** (`ALV-N026_typst-proof-kit.zip`, SHA-256 `CFDB4166A8F3DF02FD4E035986324833BD8D8E102236271FD5C66E54AA2DCE52`):
- **Contents:** the release-candidate `typst.exe`, the actual Noto Sans files, four representative templates (prescription p2 with the CANCELLED watermark, treatment-plan copy v1, statement v4, receipt v4) and synthetic inputs.
- **Pinning:** `manifest.json` pins 18 files.
- **The runner** (`run-proof.ps1`):
  - refuses on any hash mismatch;
  - renders each template twice in fresh isolated folders with the fixed issue timestamp (`1795089600` = 2026-11-19T12:00:00Z), PDF/A-2b and the section 1 and 2 environment;
  - compares each pair and checks the result against `expected-outputs.json`;
  - writes a report: engine version and hash, font hashes, arguments, Typst-relevant environment, input hash, output hashes, time zone, Windows version.

**Run 1, author machine** (Central Standard Time, UTC-06:00, Windows 10.0.26300): **PASS**, run twice.

| Template | Bytes | PDF SHA-256 | Identical |
|---|---|---|---|
| prescription-p2 | 29,368 | `b9c68508cda5c7e61702c4e62d62e19467611729d1430238c1ce73532019d007` | yes |
| treatment-plan-copy-v1 | 27,445 | `07689e545dab6e46e1cc426ba9588e2f1158a56445102cd592d093baeeb1d3c3` | yes |
| statement-v4 | 27,944 | `7db456de7bb33365b1257674f76d3945889d8136b2844bdf94a983d91a414354` | yes |
| receipt-v4 | 24,645 | `cdf31259a8b915a5d37d5c106431b84c0d72bfa645ef5d48949b29dca061ad42` | yes |

**Run 2, a second Windows PC in another time zone:** **PENDING.** The owner will run the kit. **Stop rule:** if it fails, or does not match the expected hashes, ALV-N026 stops for redesign. The byte-for-byte requirement is not weakened without a new owner and reviewer decision.

The same gate repeats at implementation with the real product templates before closure.

## 5. Legacy documents (owner decision, 2026-11-20)
- **What "p1" and "v1 to v3" are:**
  - `p1` is the prescription's canonical-content version.
  - Financial `v1`, `v2` and `v3` are the statement and receipt layout and wording versions.
  - Both are **existing controlled content definitions with content hashes**, drawn in the browser.
  - **No rendered printout was ever stored**, so no legacy printout can be rebuilt byte for byte, and none is promised.
- **Reprinting a pre-PDF record:**
  - The server regenerates the canonical content and **verifies the retained content hash first**. Financial documents have a stored `ContentHash`. For a `p1` prescription no hash was stored, so only the immutable record content itself is available; the brief must say how that is verified, or refuse.
  - On success it renders a new PDF clearly labelled: *"Reprint generated 2026-11-20 from the verified record. This is not the original browser printout."*
  - If verification fails, the reprint is refused with an integrity issue reported. Changed current data is never presented as the historical document.
- **Byte-for-byte reconstruction** applies only to documents first issued through the new controlled PDF process: prescription p2, treatment-plan copy v1, statement and receipt v4 onward.

## 6. Archival format and fonts
- **Format:** **PDF/A-2b** for newly issued archival PDFs (`--pdf-standard a-2b`), conditional on the section 4 gate. It was byte-identical in every run so far.
- **Fonts:** **Noto Sans 2.015**, from the official Noto project release `NotoSans-v2.015.zip` (github.com/notofonts/latin-greek-cyrillic, 2024-11-20, zip SHA-256 `0c34df072a3fa7efbb7cbf34950e1f971a4447cffe365d3a359e2d4089b958f5`), hinted TTF:

| File | SHA-256 |
|---|---|
| NotoSans-Regular.ttf | `478c558ea716033cd60c03438f628dfa75694dcf6b5f6d505a2f05fd2b4f3823` |
| NotoSans-Bold.ttf | `1df075a380fc7cb898acf64c1f7b3b4dd780de3caa860178bf929de35817a913` |
| Licence | `OFL.txt` (SIL Open Font License 1.1), shipped with the fonts and listed in the product's third-party notices |

- **No other fonts:** `--ignore-system-fonts --ignore-embedded-fonts`.
- **Font set per template version:** each version records its exact font set. A font change means a new template version.

## 7. Release acquisition and provenance (owner decision, 2026-11-20)
**The approved store** is an owner-controlled, versioned folder:

```
C:\.alvera-data\release-artifacts\typst\0.15.1\windows-x64\
  typst-x86_64-pc-windows-msvc.zip   (the reviewed upstream ZIP, SHA-256 19ce3551...f6736)
  SHA256SUMS.txt                     (the ZIP and the extracted typst.exe: 081217a4...b31e)
  LICENSE, NOTICE                    (Apache-2.0 and the third-party notices, from the ZIP)
  SOURCE.txt                         (upstream URL, tag v0.15.1, commit 9dfd3a08, download date)
  APPROVAL.md                        (this review's approval reference)
```

A matching `C:\.alvera-data\release-artifacts\fonts\noto-sans\2.015\` folder holds the fonts, `OFL.txt` and their hashes.

**Rules:**
- The release script **only copies** from these folders and refuses a missing file or mismatched hash.
- The product build never downloads anything from the internet.
- An approved version folder is never overwritten in place; a new version gets a new folder.
- The API also verifies the engine hash at startup and before rendering: `pdf_engine_untrusted` (503), with no fallback engine.
- A private GitHub Release may replace the folder later, once the release process is ready for that step (owner).

The folders will be created **after** approval, since `APPROVAL.md` must reference it.

## Unchanged from R01
- The failure handling: 30 s timeout and kill; one retry for a crash or timeout only; a concurrency cap of 2; stable error classes; redacted stderr; idempotent renders.
- The required test list, extended by sections 1 to 5:
  - the import-lint test;
  - the hash-mismatch refusal;
  - the `integrity_mismatch` refusal;
  - the legacy-reprint label;
  - the golden files for each template version.
