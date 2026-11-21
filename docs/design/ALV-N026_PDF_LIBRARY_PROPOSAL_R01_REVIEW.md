# ALV-N026 PDF library proposal R01 — independent review response

**Decision:** CHANGES_REQUIRED before approval for implementation.

**Reviewed proposal:** `docs/design/ALV-N026_PDF_LIBRARY_PROPOSAL-R01.md`  
**Scope:** selection and safe use of a server-side PDF engine for ALV-N026.  
**No implementation is authorized by this response.** Revise the proposal and provide the requested isolated evidence first.

## Decision summary

Typst is the leading candidate and remains acceptable in principle. Its Apache-2.0 licensing, local execution model, pinned-binary proposal, measured same-machine deterministic output when `--creation-timestamp` is fixed, and ability to package fonts are all positive.

Approval is withheld because the proposal must close the control gaps below. These are conditions for selecting the engine, not a request to begin product integration.

## Required revisions and evidence

### 1. Network isolation must be proven, not delegated to a firewall fallback

The normal product control must demonstrate that Typst cannot make network calls in DentalKeep's configured execution environment. Explicitly control package, cache, data, and configuration locations. Provide a focused proof that rendering a product template does not attempt or require network access.

A Windows firewall rule may be documented as defense in depth only. It is not sufficient as the primary guarantee, and it must not be the only answer if the engine's behavior is uncertain.

### 2. Resolve the file-access boundary

The proposal currently combines a per-render `--root` directory with templates and fonts located elsewhere. Revise the design to state—and prove—which exact paths the process can read and write:

- approved immutable template version;
- approved bundled font files;
- current render's input JSON and output PDF only;
- no broad host filesystem access;
- no caller-controlled path, command argument, template selection, or import.

The proof may use an intentionally inaccessible/unapproved file and show that a render cannot read it. Keep user data out of command-line arguments.

### 3. Protect the historical render-input snapshot as patient data

The proposed canonical JSON input snapshot is patient data. The revised proposal must specify:

- storage location and encryption/protection at rest;
- access permission for viewing or rebuilding a historical document;
- audit events for a rebuild and for any generated/retrieved historical output;
- retention, deletion/inactivation, backup, export, and patient-merge behavior;
- why the snapshot is necessary and how it avoids becoming an uncontrolled duplicate clinical-data store.

If a stored source-document snapshot or another existing controlled record can meet deterministic rebuilding needs, prefer it over a new unrestricted data store.

### 4. Strengthen the determinism gate

Before implementation approval/closure, demonstrate byte-identical rendering with the actual release candidate binary, actual bundled fonts, and each representative document/template version:

1. two independent processes in clean working folders;
2. a second clean Windows environment or Windows machine in a different time zone;
3. the fixed stored document issue timestamp, not the render time.

Record engine version, binary hash, font hashes, command arguments, environment variables relevant to Typst, input hash, output hash, and result for each run.

If this proof fails, stop for redesign. Do not weaken the historical byte-for-byte rebuild requirement without a new owner/reviewer decision.

### 5. Clarify the legacy-template statement

Define exactly what `prescription/p1` and financial template versions `v1` through `v3` mean:

- whether they are existing controlled server-rendered document templates; or
- newly introduced PDF template representations.

Do not promise byte-for-byte rebuilding of legacy browser printouts unless DentalKeep already retained an exact controlled server-side artifact and deterministic inputs for them. The proposal must distinguish historical-document preservation from new server-PDF issuance going forward.

### 6. Set archival format and font choice

Propose **PDF/A-2b** as the standard for newly issued archival PDFs, conditional on the same determinism proof above.

Use **Noto Sans under the SIL Open Font License** as the initial bundled font family unless the revised proposal identifies a documented compatibility reason to use another redistributable font. Record the exact font files, license notice, and SHA-256 hashes.

### 7. Fix the release acquisition and provenance route

Select one route now: the reviewed Typst executable, its SHA-256, Apache-2.0 license, and third-party notices must be stored in the approved internal release-artifact store and retrieved only through the controlled release pipeline.

Do not leave “artifact store or vendored” undecided. The product build must not download the engine from the public internet.

## Required next deliverable

Submit `ALV-N026_PDF_LIBRARY_PROPOSAL-R02.md` with each requirement mapped to its design change and focused scratch evidence. Keep all spikes outside the repository. Do not add Typst, templates, fonts, PDF code, installer changes, migrations, or production configuration until R02 is approved.

## Review boundary

This response approves neither a dependency nor a production implementation. It preserves the owner decision for server-side PDF generation and keeps Typst as the leading candidate pending R02.
