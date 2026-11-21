# ALV-N026 G4: release-store creation record

**Date:** 2026-11-21. **Session:** CC-20261115-m7x2. **Status:** created and verified; submitted to the reviewer to close G4.

**Authority:**
- `ALV-N026_R03_FINAL_DESIGN_REVIEW.md` (G1 APPROVED: "create the owner-approved versioned release-artifact folders");
- the owner's store decision of 2026-11-20 (proposal R02 item 7);
- the owner's Go of 2026-11-21.

## What was done
- **The location** is `C:\.alvera-data\release-artifacts\`. It did not exist before, and the script refuses to run if it does.
- **Files were copied only.** Every source was hash-checked before copying and re-hashed after. Any mismatch would have stopped the run and removed the copy. Nothing was overwritten.
- **`vc_redist.x64.exe`'s signature** was checked before copying: Authenticode Valid, signer `O=Microsoft Corporation`.
- **Nothing was added to the repository or the build.** Using the store in the build is slice S4.

## Contents (SHA-256 as listed by the creation run)
| Folder | File | SHA-256 |
|---|---|---|
| `typst\0.15.1\windows-x64` | `typst-x86_64-pc-windows-msvc.zip` | `19ce3551153c2fe7ee9fa2f95208310c8f4d3209fedb699e0333faf8913f6736` (approved) |
| | `LICENSE` | `62c7a1e35f56406896d7aa7ca52d0cc0d272ac022b5d2796e7d6905db8a3636a` |
| | `NOTICE` | `1778244777547c281b6f5fa9fc0c18ab21f8d4491c803f64e09046800f5fcb26` |
| | `SHA256SUMS.txt` | Lists the zip, LICENSE and NOTICE, and the files inside the zip, including `typst.exe` `081217a463adb006f8894b44227fe4b9c9e91fc85f5463d5948e8370db9bb31e` (approved) |
| | `SOURCE.txt`, `APPROVAL.md` | Upstream URL, tag v0.15.1, commit 9dfd3a08, download date, licence, runtime prerequisite; the approval reference and the store rules |
| `fonts\noto-sans\2.015` | `NotoSans-v2.015.zip` | `0c34df072a3fa7efbb7cbf34950e1f971a4447cffe365d3a359e2d4089b958f5` (approved) |
| | `NotoSans-Regular.ttf` | `478c558ea716033cd60c03438f628dfa75694dcf6b5f6d505a2f05fd2b4f3823` (approved) |
| | `NotoSans-Bold.ttf` | `1df075a380fc7cb898acf64c1f7b3b4dd780de3caa860178bf929de35817a913` (approved) |
| | `OFL.txt` | `cee9892f9f0cc8fe882c9e9537ee6a89621d86ee7ceaf70b02e2b2b1c25c061a` |
| | `SHA256SUMS.txt`, `SOURCE.txt`, `APPROVAL.md` | |
| `msvc-runtime\14.44.35211.0\x64` | `vc_redist.x64.exe` | `cc0ff0eb1dc3f5188ae6300faef32bf5beeba4bdd6e8e445a9184072096b713b` (approved), Authenticode Valid, Microsoft |
| | `PROVENANCE.txt` | The R05 provenance record: source folder, the set 14.44.35112 vs file 14.44.35211.0 reconciliation, redistribution basis, the owner's entitlement |
| | `SHA256SUMS.txt`, `APPROVAL.md` | The approval and the owner's affirmation of 2026-11-21 |

**Sources of the copies:**
- the Typst zip and Noto zip from the 2026-11-21 (UTC) engine-spike downloads, kept in the session scratch folder;
- the fonts from the approved proof kit;
- `vc_redist.x64.exe` from the owner's Visual Studio 2022 redistributable folder (`VC\Redist\MSVC\14.44.35112\`).

**The creation script** is in the session scratch folder (`create-release-store.ps1`). It is not part of the product.

## Rules the store follows (as approved)
- The release script only copies from these folders. It refuses a missing file, a hash mismatch, or (for the redistributable) a signature that is not Valid with a Microsoft signer.
- A version folder is never overwritten; a new version gets a new folder.
- The product build never downloads anything.

## Requested
Reviewer confirmation that G4 is closed, so that S4 (the PDF renderer service using the store) may begin.
