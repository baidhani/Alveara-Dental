# ALV-N026 G4 release-store record R02: access restricted (response to `ALV-N026_G4_RELEASE_STORE_REVIEW.md`)

**Date:** 2026-11-21. **Session:** CC-20261115-m7x2. **Status:** submitted for G4 closure. Owner Go for the ACL change: 2026-11-21 (no read access for other local users).

## 1. Access control (review item 1)
| Property | Value |
|---|---|
| Owner | `FEERCY-LEGION7I\firas` (the release-store owner) |
| Inheritance on `C:\.alvera-data\release-artifacts` | **Protected** (`AreAccessRulesProtected = True`): nothing is inherited from `C:\.alvera-data` |
| Entries on the root, applied to all subfolders and files `(OI)(CI)` | `BUILTIN\Administrators`: Full Control; `NT AUTHORITY\SYSTEM`: Full Control; `FEERCY-LEGION7I\firas`: Full Control |
| Everything below the root (26 items) | Inherits exactly those three entries (`(I)`). A scripted check over all 26 items found 0 with any other entry and 0 with `Users`, `Authenticated Users` or `Everyone` |
| Removed | `Authenticated Users: Modify` and `Users: Read and Execute` (both previously inherited) |
| Build or release identity | The same account (`firas`), so no separate read entry is needed. A different build account would need a Read and Execute entry added, recorded as a change |

**Commands used:**
1. `icacls <root> /inheritance:r /grant:r <firas>:(OI)(CI)F SYSTEM:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F /T /C /Q`
2. `icacls <root>\* /reset /T /C /Q`

**Disclosure:** the first command's `/T` also removed the inherited entries from the files below the root, leaving them with no entries; their owner kept implicit control. The second command reset them to inherit from the protected root. The state above is the final, verified one.

**Reversal:** `icacls <root> /reset /T` restores inheritance from `C:\.alvera-data`.

## 2. Artifacts unchanged
Re-hashed after the ACL change; all match the approved values:
- the Typst zip `19ce3551…f6736`;
- the Noto zip `0c34df07…58f5`;
- Regular `478c558e…3823`;
- Bold `1df075a3…a913`;
- `vc_redist.x64.exe` `cc0ff0eb…b713b`.

Authenticode: **Valid**, signer `CN=Microsoft Corporation, O=Microsoft Corporation`.

**No overwrite:**
- the store holds the same 17 files as at creation;
- the latest file write is from the creation run (2026-11-21 21:03:37 UTC);
- no artifact or version folder was replaced. The ACL change alters permissions only, not file contents.

## 3. The trusted-manifest rule for the release and build integration (review item 2; implemented in a later approved slice, not now)
- **The build compares each artifact against exact SHA-256 values in a reviewed, version-controlled manifest in the repository** (for example `build/release-artifacts.manifest.json`, changed only by a reviewed commit). The `SHA256SUMS.txt` files in the store are inventory only and are never the trust source.
- **`vc_redist.x64.exe`'s signature** is checked at use time (Valid, signer Microsoft Corporation), as well as its hash.
- **Any missing, mismatched or wrongly signed artifact refuses the build or install step.** There is no fallback and no download.
- **The manifest itself** belongs to slice S4 (the PDF renderer and release integration), under its own review.

## Requested
G4 closure.
