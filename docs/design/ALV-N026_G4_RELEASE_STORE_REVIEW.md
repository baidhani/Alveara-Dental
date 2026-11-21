# ALV-N026 G4 release-store record — independent review response

**Decision:** CHANGES_REQUIRED. G4 remains open.  
**Reviewed record:** `docs/design/ALV-N026_G4_RELEASE_STORE_RECORD.md`  
**Verified store:** `C:\.alvera-data\release-artifacts\`

## Verified positive evidence

The three actual artifacts match the approved values:

- Typst ZIP SHA-256: `19ce3551153c2fe7ee9fa2f95208310c8f4d3209fedb699e0333faf8913f6736`;
- Noto Sans ZIP SHA-256: `0c34df072a3fa7efbb7cbf34950e1f971a4447cffe365d3a359e2d4089b958f5`;
- Noto Sans Regular/Bold file hashes match the approved values;
- Microsoft `vc_redist.x64.exe` SHA-256: `cc0ff0eb1dc3f5188ae6300faef32bf5beeba4bdd6e8e445a9184072096b713b`;
- the Microsoft executable's Authenticode signature is Valid and the signer is Microsoft Corporation.

The folder layout, notices, provenance/approval records, and never-overwrite intent are also appropriate.

## Blocking problem: the current folder is not owner-controlled

The actual root folder inherits these effective permissions:

```text
Authenticated Users: Modify
Users: Read and Execute
Administrators: Full Control
SYSTEM: Full Control
```

`Authenticated Users: Modify` allows any authenticated local account to replace an artifact and its adjacent `SHA256SUMS.txt`. Therefore the current store does not meet the approved owner-controlled-release-store rule.

## Required remediation before G4 closure

### 1. Restrict release-store write access

Protect the ACL on `C:\.alvera-data\release-artifacts\` and propagate it to its contents. Remove inherited broad modify/write rights, including `Authenticated Users: Modify`.

The intended access model is:

| Identity | Permission |
|---|---|
| Named release-store owner (`firas`) | Full Control |
| `SYSTEM` | Full Control |
| `Administrators` | Full Control |
| The specific release/build identity, if different | Read and Execute only |
| Other local users | No write permission; Read and Execute only if operationally necessary |

Do not use broad `Users`, `Authenticated Users`, or `Everyone` write/modify permissions. Record the resulting owner, inheritance-protection state, and effective access entries in the G4 record.

### 2. Use a trusted expected-artifact manifest

The `SHA256SUMS.txt` files inside the same mutable store are useful inventory, but cannot be the sole trust source. The future release/build integration must compare artifacts against exact expected SHA-256 values in a separately reviewed, version-controlled build manifest (or an equivalently protected signed manifest), not merely against a checksum file stored beside the artifact.

For `vc_redist.x64.exe`, also verify a Valid Microsoft Authenticode signature at use time. A missing, mismatched, or incorrectly signed artifact must refuse the build/install step.

The build-manifest code/configuration belongs to the later approved PDF/release integration slice; it is not authorized by this G4 remediation.

### 3. Re-submit closure evidence only

After the ACL correction, submit a concise G4 R02 record containing:

- owner, inheritance-protection state, and effective ACL entries;
- the unchanged artifact hashes and Microsoft signature result;
- confirmation that no artifact/version folder was overwritten; and
- the planned reviewed-build-manifest rule from item 2.

No engine, installer, migration, or product code work is authorized while G4 is open.
