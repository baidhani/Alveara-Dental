# ALV-N026 PDF library proposal (R04): fidelity standard, versioned regeneration, runtime readiness

**Story:** ALV-N026 (item 63, PKG-12). **Date:** 2026-11-21. **Status:** AWAITING_REVIEW (design only). Nothing has been added to DentalKeep.

**Companion document:** the pre-implementation brief, `ALV-N026_PRE_IMPLEMENTATION_BRIEF.md`. It carries the per-material data model and the tests; this proposal covers the engine and the rules every material follows.

**Governing owner decisions (2026-11-21):**

| Document | Effect |
|---|---|
| `ALV-N026_PDF_OUTPUT_FIDELITY_REQUIREMENT_DECISION.md` | **Byte-for-byte PDF equality is not a requirement anywhere.** It supersedes the byte-identity language of R01 to R03, of the R03 review and its Addendum 01, and of OQ-2's "byte-for-byte" wording |
| `ALV-N026_PDF_LIBRARY_PROPOSAL_R03_REVIEW_ADDENDUM_01.md` | No issued-PDF store: documents are regenerated from versioned sources, or refused |
| `ALV-N026_REGENERATION_SOURCE_SURVEY.md` | Decisions D1 to D5 (per-material sources) |

**Decision requested:** approve Typst 0.15.1, with Noto Sans 2.015, PDF/A-2b and the installer-managed Microsoft Visual C++ runtime, as the server-side PDF engine, under:
- the fidelity standard (section 1);
- the regeneration contract (section 2);
- the runtime readiness rules (section 4);
- the unchanged isolation controls (section 5).

## What changed from R03
| R03 position | R04 position |
|---|---|
| Byte-identical regeneration as the historical guarantee; golden-file **byte** gate per release | **Content fidelity** (section 1). Byte comparison is an optional diagnostic for a fixed bundle only |
| "Runtime-version byte drift is a stop condition" | Superseded. An engine, runtime, font or platform update may change PDF bytes. It is acceptable while the fidelity checks pass |
| R03 review (CHANGES_REQUIRED): store every issued PDF | Superseded by Addendum 01 and the fidelity decision: no PDF store (section 2) |
| Installer: exit codes `0`, `1638` and `3010` count as success | **Success is proven only by the readiness preflight** (section 4, R03 review item 4) |
| Legacy and prescription-status rules sketched | Settled by the owner as D2 and D4, and carried in the brief |

## 1. Fidelity standard (product requirement)
Every issued or regenerated document must show patient, practice, clinical, financial, prescription and status data that is **accurate for its applicable point in time**, and must use its recorded template version and its issue or regeneration date.

**Sources:** historical content comes only from immutable or versioned controlled records, never from silently substituted current values.

**Refusal:** if the historical source cannot be reconstructed and verified, the request is refused with `integrity_mismatch`. A misleading document is never produced.

**Audit:** issue, reprint and regeneration events record the action, actor, time, source and version identity, and the integrity outcome. The audit log stays PHI-free.

**Verification** checks data and rendered content, not raw bytes:
- canonical source-data checks (with a canonical-content hash where suitable);
- PDF text-extraction assertions on identifiers, MRN, dates, amounts, services, prescriber and practice, status, watermark and notices;
- calculation and formatting checks;
- template-selection checks;
- layout and visual checks that tolerate encoding and metadata differences.

## 2. Regeneration contract (no PDF store)
- **No generated PDF is retained.** Temporary render files are deleted.
- **Each issue writes an immutable issue row** holding:
  - the material identity;
  - the issue time;
  - the template version;
  - the render bundle id (engine, font set and runtime versions);
  - the source cut-offs (for example ledger numbers, event or state sequence, plan revision);
  - a small envelope capturing values that have no history (practice details, and identity text as displayed);
  - optionally, a canonical-content hash.
- **Regeneration rebuilds** the canonical content from those cut-offs and the envelope, and renders it with the **recorded template version** using the **current approved bundle**. The regenerated copy keeps the original issue date and template version. It records the bundle it was regenerated with, and may be labelled as a regenerated copy.
- **Where a value has no history and no envelope,** the material is not regenerated as historical (owner D2 and D4).

The per-material mapping, settled by the owner, is in the brief:

| Material | Rule |
|---|---|
| Statement and receipt | The existing `FinancialDocument` contract |
| Prescription | A new `PrescriptionIssue` row |
| Legacy `p1` prescription | Refuse a historical reprint; offer a new p2 record copy |
| Treatment-plan copy | A server-built `TreatmentPlanCopyIssue` row |
| Clinical record copy | A new issue as of now only |

## 3. Engine, fonts, format (unchanged from R03)
| Component | Detail |
|---|---|
| Engine | Typst 0.15.1. Upstream ZIP `19ce3551…f6736`, `typst.exe` `081217a4…b31e`, Apache-2.0 |
| Fonts | Noto Sans 2.015, Regular `478c558e…3823` and Bold `1df075a3…a913`, SIL OFL 1.1. No system or embedded fonts |
| Format | PDF/A-2b |
| Templates | Versioned, server-chosen and hash-pinned. A font change means a new template version |
| Engineering evidence | The accepted cross-machine proof (kit R05, VM run 2) remains engineering evidence that Typst renders deterministically with a fixed bundle. Under the fidelity decision it is **not** a continuing product requirement, and no further byte proof is required |

## 4. Runtime prerequisite and readiness (R03 review item 4)
Typst needs `VCRUNTIME140.dll` (the evidence is in kit R05). Production gets it from the **official Microsoft Visual C++ Redistributable, installed and serviced centrally**. DentalKeep ships no app-local copy.

1. **Install (ALV-N013 installer scope, not authorized here).**
   - Run the release-store `vc_redist.x64.exe` (`cc0ff0eb…b713b`, Authenticode Valid, Microsoft) with `/install /quiet /norestart`, after re-verifying its hash and signature.
   - **An exit code alone never proves success.** After `0`, `1638` or `3010`, the installer runs the **readiness preflight** (step 2). Only a passing preflight completes the PDF component. Any other exit code, or a failed preflight, fails the installation with a clear message.
   - `3010`: the installer reports "restart required", and the preflight runs again at first application start after the restart. Until it passes, PDFs are unavailable (step 3).
   - Re-running the installer is safe.
2. **Readiness preflight.** This is the same check as in the proof kit. It runs at installation, at API startup, and before each render batch:
   - the `typst.exe` hash equals the pinned hash;
   - `typst --version` starts within the timeout, exits 0, and prints exactly `typst 0.15.1 (9dfd3a08)`;
   - the result and the installed runtime version are logged, and the runtime version is recorded in the bundle id.
3. **Failure.** PDF issuing is unavailable:
   - the errors are `pdf_engine_unavailable` (503) for a start or runtime failure and `pdf_engine_untrusted` for a hash mismatch;
   - there is no fallback engine and no browser-print substitute for controlled documents;
   - the rest of the application, including offline work, is unaffected;
   - the administrator's System Status shows the state and the remedy (re-run the installer or repair the redistributable).
4. **Runtime updates by Microsoft Update.** They are allowed. The bundle id records the runtime version used for each issue and regeneration. A release, or a detected runtime change, runs the fidelity checks (section 1). A **content** or layout difference stops the work for review; a byte-only difference does not.
5. **Licence.** Visual Studio 2022 Distributable Code ("Visual C++ Runtime Files"). The release owner affirmed entitlement on 2026-11-21, and it is recorded in the release store's `APPROVAL.md`.

## 5. Isolation and file boundary (unchanged from R02/R03)
- **Isolation per render:**
  - template import lint;
  - empty package and cache folders;
  - every proxy variable set to `http://127.0.0.1:9`;
  - risky variables removed;
  - a 30-second timeout and kill.
- **Folder per render:** a fresh ACL-restricted folder used as `--root`, fonts supplied only through `--font-path`, no shell, server-chosen paths and arguments, and the folder deleted in `finally`.
- **Defence in depth:** the firewall rule and the service account (ALV-N013).

The disclosed limit is unchanged: there is no socket-level proof.

## 6. Release store (owner decision 2026-11-20; R03 section 7, unchanged)
`C:\.alvera-data\release-artifacts\` holds Typst 0.15.1, Noto Sans 2.015 and the `vc_redist.x64.exe` 14.44.35211.0 folders.
- The release script only copies, and refuses a missing file, a hash mismatch or (for the redistributable) a bad signature.
- A version folder is never overwritten.
- **The folders are created only after this proposal is approved.**

## 7. Failure handling and tests (engine level)
**Unchanged:**
- a 30-second timeout and kill;
- one retry, for a crash or timeout only;
- a concurrency cap of 2;
- stable error classes and redacted stderr;
- idempotent issue keys.

**Engine-level tests:**
- the import lint;
- the template-hash mismatch refusal;
- the readiness refusal (engine not startable, wrong version, hash mismatch);
- the timeout and kill;
- the concurrency cap;
- the isolation environment;
- fidelity assertions on a fixed sample per template.

The per-material tests are in the brief.

## 8. Open items (each is in the brief)
- **PD-11 research:** the exact official US sources, fields and exclusions. It needs web research, which the owner must permit. It is independently reviewed before any prescription-content code.
- **PD-24 `ManageDocuments`:** a permission-model change (escalation).
- **ALV-N013 installer items:** the redistributable step, readiness at install, the firewall rule and the service account. **Not authorized by ALV-N026.**
- **Release-store folders:** created after approval.
