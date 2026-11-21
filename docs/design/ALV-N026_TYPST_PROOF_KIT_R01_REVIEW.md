# ALV-N026 Typst proof kit R01 — independent review response

**Decision:** CHANGES_REQUIRED. Do not distribute or run the current kit on the second machine.

**Reviewed artifact:** `ALV-N026_typst-proof-kit.zip`  
**Artifact SHA-256 reviewed:** `CFDB4166A8F3DF02FD4E035986324833BD8D8E102236271FD5C66E54AA2DCE52`  
**Purpose:** second-Windows-machine, different-time-zone determinism proof for the proposed Typst PDF engine.

## Positive findings

The kit is appropriately self-contained. It contains a pinned Typst executable, Noto fonts and license notices, representative templates, synthetic inputs, expected output hashes, a manifest, and a proof script. The reviewed inputs contain no apparent real patient information. The script uses two separate temporary render folders per template, a fixed issue timestamp, bundled fonts, and PDF/A-2b output.

These strengths do not yet establish artifact provenance, network isolation, or the intended file-access boundary.

## Required R02 changes

### 1. Authenticate the kit before extraction

`manifest.json` checks files inside the kit, but cannot authenticate itself or the included script if an entire kit were altered. The R02 README must require the receiving operator to calculate the ZIP's SHA-256 **before extraction** and compare it to a checksum supplied through a separate trusted channel.

For this R01 artifact, the separately communicated checksum is:

```text
CFDB4166A8F3DF02FD4E035986324833BD8D8E102236271FD5C66E54AA2DCE52
```

For R02, publish its newly calculated ZIP SHA-256 in the review record and instruct the operator to stop on a mismatch. Do not rely solely on a self-contained manifest.

### 2. Add a real no-network negative control

The current proxy variables are defense in depth; they do not prove that Typst cannot make a direct network connection. Add focused proof that an external `@preview` package import is refused under the controlled empty package/cache configuration. Record the expected refusal and the relevant configuration in the result JSON.

Also add the permanent template-lint design requirement: frozen product templates must reject any external package import, including `#import "@..."`. This control is required in the later implementation proposal; it does not authorize implementation now.

### 3. Prove the file-access boundary

Add a negative-control template that tries to read a deliberately created, unapproved file outside the per-render temporary root. The controlled run must refuse access. Record the result without exposing local machine paths or file contents.

The R02 evidence must demonstrate that the engine may access only:

- the approved immutable template version;
- the approved bundled fonts;
- the current render's JSON input and PDF output; and
- explicitly controlled package/cache directories.

No caller-controlled path, command argument, template selection, or external import may expand that boundary.

### 4. Expand the result record

The results JSON must record actual effective values—not only a narrative summary—for:

- engine version and SHA-256;
- each font hash;
- each template hash;
- each input hash;
- expected and actual PDF hashes and byte sizes;
- fixed issue timestamp;
- Windows version, PowerShell version, time-zone ID and UTC offset;
- controlled Typst root, package, cache, and font-path values, represented without user-specific absolute paths;
- outcomes of the no-network and file-boundary negative controls.

Continue to omit patient data, usernames, and local filesystem paths.

### 5. Correct the script comment

Correct the path typo in the introductory script comment from `%TEMP%\typst-proof-results` as currently malformed by an embedded tab to the intended literal location:

```text
%TEMP%\typst-proof-results
```

The current actual output location is correct; this is a documentation clarity correction.

## R02 delivery and run conditions

Submit a rebuilt `ALV-N026_typst-proof-kit` R02 ZIP and its separately stated SHA-256 for review. Do not run the cross-machine proof until the revised kit is approved.

After R02 approval, the second-machine operator should verify the ZIP checksum before extraction, run only the supplied proof command, and return the results JSON. The second Windows machine must use a time zone different from Central time.

## Review boundary

This is an evidence-kit review only. It does not approve Typst integration, a product dependency, fonts, templates, installer changes, or production PDF implementation.
