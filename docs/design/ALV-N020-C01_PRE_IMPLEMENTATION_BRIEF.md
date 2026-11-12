# ALV-N020-C01 Pre-Implementation Briefing (revision 3)

**DentalKeep Login Presentation, Managed Local Background Catalog, and Configurable Footer Notice**

- **Plan item:** 57a, `GPT Docs/Alveara_Dental_Execution_Plan_v4.md` as amended for the managed-catalog design (committed `6a7d8c0`)
- **Governing decisions:** `docs/design/ALV-N020-C01_SCOPE_CHANGE_REQUEST.md` (owner's request) and `docs/design/ALV-N020-C01_SCOPE_CHANGE_REVIEW.md` (APPROVED WITH REQUIRED DESIGN CORRECTIONS, option A)
- **Depends on:** ALV-N020 (COMPLETE, closure `4ada83a`)
- **Session:** CC-20261109-8w3d
- **Revision 1** (`3e1a3d2`) proposed a fixed bundled catalog. **Revision 2** (`d92f6ef`, `ae0852b`, `19135f1`) was reviewed in `.alveara/reviews/ALV-N020-C01/BRIEFING-R02.md` (CHANGES_REQUIRED, four corrections). This revision 3 answers those four: no inferred permission holder on an existing installation (section 4.1), raw-entry identifiers removed (section 0), the test-permission statement (section 8), and the tile-readiness gate (section 2.3).
- **Status:** briefing only. No application code, schema, configuration, asset, dependency or test has been changed, and no test has been run. Implementation waits for independent approval of this revision. This revision contains no implementation or test-run evidence.
- **Regression route:** post-Gate-E deferred-regression exception: focused API, persistence, security, browser, accessibility and recovery evidence for this package; broad regression `DEFERRED_TO_ALV-N028`.
- **Not in this item:** automatic rotation (`ALV-N029`, deferred); the configuration busy-state finding F-075 (stays with `ALV-N027`).

## 0. Raw source entries

No Manual E2E raw entry drives this item, so none is named here; the item comes from the owner's plan amendment and scope change. (Earlier sign-in-page findings were closed by ALV-N020 and are only preserved, not changed, by this item: sections 1.2 and 6 list the behaviors that stay unchanged.) The raw source entries for this item are the owner's and the reviewer's own records, quoted:

| # | Date | Owner's words / decision |
|---|---|---|
| O-1 | 2026-11-12 | Plan amendment items 57a and 65a (committed `db138ec`, revised `6a7d8c0`). |
| O-2 | 2026-11-12 | "D-1 keep the logo as it for now"; later "use the provided logo for now" (the transparent-background version the owner sent). |
| O-3 | 2026-11-12 | "D-2 i have the rights for them just use them." |
| O-4 | 2026-11-12 | Default notice "for USA": the 610-character text in section 5.5. |
| O-5 | 2026-11-12 | Mockup: background fills the page; a full-height light centre panel holds the logo, the fields, the button and the footer notice at its bottom. |
| O-6 | 2026-11-12 | "I just added a fifth tile ... and I might add more ... make sure that the system is able to scan and find the new added images." |
| O-7 | 2026-11-12 | "the admin can click a add back ground image, select the image from desk, added it to the collection, specify the display mode to tile, streach, fill." |
| O-8 | 2026-11-12 | Transparent logo: `C:/Users/firas/Desktop/Epic/dentalKeep logo.png`; permission to create a cleaned copy with a 'v2' suffix in the same folder for review. |
| O-10 | 2026-11-12 | "v2 approved" (the cleaned logo L3). |
| O-9 | 2026-11-12 | Initial presentation administrator: "add it to my admin account if you asking for the current available accounts. if you asking for the future during deployment then it would be the first admin account going to be created during deployment." |
| R-1 | 2026-11-12 | Reviewer: option A approved with seven binding corrections; default = the teal doodle tile; labels stay "Username" and "Sign in"; the logo artwork is approved; notice up to 1,000 characters. |
| R-2 | 2026-11-12 | Reviewer (`BRIEFING-R02.md`): CHANGES_REQUIRED: never infer the holder on an existing installation; reproduce full raw entries or remove the identifiers; every test group needs the owner's explicit permission unless the owner adopts a separate written standing rule; close tile readiness with tiled browser screenshots before packaging assets. The cleaned logo v2, Tile/Stretch/Fill, the teal default and the wording are accepted. |

## 1. Intended result

### 1.1 What a person sees
- The sign-in page: the chosen background fills the page; a full-height light centre panel (owner mockup) holds the DentalKeep logo, the visible labels **Username** and **Password**, the **Sign in** button and, at the bottom of the panel, the plain-text footer notice when it is turned on. The panel is solid, so the background never sits behind text. Error, loading and "Local server unavailable" states read as they do today.
- The designated presentation administrator opens **Practice configuration -> Sign-in screen**: picks a background from the catalog (built-in and added images, each with a thumbnail), picks **Tile**, **Stretch** (with the visible note "May distort the image") or **Fill** ("Recommended for photographs"), clicks **Add background image** to add one from their computer, can make an added image unavailable (never delete it), and edits the footer notice and the **Show footer notice on login screen** setting.
- Everyone else sees no Sign-in screen tab; the server refuses them anyway.

### 1.2 Technical
- A managed local catalog: five bundled images plus administrator-added images that the server decodes and re-encodes into clean, metadata-free derivatives; only derivatives are stored and served.
- A new narrow permission `ManageLoginPresentation`, held per named account (not per role), initially by the owner-designated account only.
- One anonymous read endpoint for the presentation and one anonymous image endpoint that serves only the currently selected derivative.
- A new backup asset class for the derivatives, with a restore check.
- No change to authentication, account-enumeration resistance, lockout, CSRF, session expiry/revocation, sign-out or the ALV-N020 outage behavior.

## 2. Asset inventory

### 2.1 Owner-supplied files (read in place in `C:\Users\firas\Desktop\Epic\`; nothing copied yet)
| # | File | Role | Pixels | Size | SHA-256 | Notes |
|---|---|---|---|---|---|---|
| L1 | `dental-keep-v01.png` | Logo, original | 1774 x 887, fully opaque, light grey #F7F6F6 background | 423,886 B | `acfaecd34fb9558f1ccb87a6dec42bc6d2593dd370071fc301ca7e94d86e3638` | Reads "KEEP dental" (two tooth outlines form "EE"); approved artwork (O-2, R-1) |
| L2 | `dentalKeep logo.png` | Logo, transparent original (O-2, O-8) | 1774 x 887, RGBA | 598,071 B | `99b36e687c8cc67b9529af5e8db9982e715daa67e404e239b79f77e7e18d93ce` | Background removed by the owner; about 32,600 faint pixels with saturated cyan/red colour values remained around the letters (visible as specks in viewers that ignore transparency) |
| L3 | `dentalKeep logo v2.png` | **Logo to use: approved by the owner (O-10)** | 1774 x 887, RGBA | 121,992 B | `ac8142697e5fe31773b9ad75008e7474e6b1bf4e5f71de3338a918a259b7b9c3` | Cleaned copy made with the owner's permission (L2 untouched): the 10 solid letter shapes kept, isolated specks removed, anti-aliased edge pixels within 2 px kept and recoloured to their letter's colour (teal (87, 173, 177), coral (201, 111, 86)). Script recorded for the handoff |
| T1 | `ChatGPT Image Nov 10, 2026, 10_41_23 AM.png` | Background "Teal doodle" - **deployment default** (R-1) | 1254 x 1254 | 1,676,595 B | `5dd15d6b763f67e9946bd722d7280fdbd99023f7f0ad4e62580fd501a9627699` | edges seamless (2.1 / 2.8 vs baseline 1.8) |
| T2 | `ChatGPT Image Nov 10, 2026, 04_00_35 PM.png` | "Blue doodle" | 1254 x 1254 | 1,946,848 B | `78f9a1f8b53b609e3adc25dd3fd2cd9bb1fe644cdfee98a360d4ddf6de1ddd43` | seamless (5.3 / 4.2 vs 5.9) |
| T3 | `ChatGPT Image Nov 10, 2026, 04_01_00 PM.png` | "Blue operatory" | 1254 x 1254 | 1,527,257 B | `33084e9d07bc05721b18036ad1e59d6641a24a81e19eb8caa7e57879e6c1bace` | left/right seamless; top/bottom 8.9 vs 2.0: **open, tile-readiness gate (2.3)** |
| T4 | `ChatGPT Image Nov 10, 2026, 04_01_13 PM.png` | "Violet operatory" | 1254 x 1254 | 1,538,167 B | `17c73a7a5754d0e78e451b9fed29002b1ab3fd7d06d979ef66dbfc3ed18da321` | seamless (1.5 / 4.5 vs 12.7) |
| T5 | `ChatGPT Image Nov 12, 2026, 01_05_55 PM.png` | "Neon doodle" | 1254 x 1254 | 1,374,863 B | `3c7f151e002ef17efad928dd4a3013d2dfcb2d66ac1b7339ea510399819b32b7` | top/bottom seamless (2.4 vs 2.4); left/right 5.9 vs 2.4: **open, tile-readiness gate (2.3)** |

Edge numbers: mean absolute RGB difference between opposite edges, against the image's own neighbouring-column difference (measured with Python/PIL).

**Source and licence:** AI-generated by the owner with ChatGPT (2026-11-10 and 2026-11-12); the owner confirms the rights (O-3). The third-party reference images in the same folder (Epic screens, a WhatsApp background, a wallpaper download) are not used.

**Bundled form:** each bundled background is prepared once with the same pipeline as uploads (section 4.2): re-encoded, metadata-free, tiles reduced to a 627 x 627 repeat unit (target under 250 KB); the logo cropped to its ink area plus margin, about 280 x 140 CSS px at 2x (target under 60 KB). They live in `src/alveara-client/src/assets/login/` (bundled by Vite, same origin, content-hashed names). Originals are not committed; the SHA-256 values above are the provenance record.

**Fallback:** each background has a solid colour equal to its dominant colour (T1 #042B30, T2 #0D314E, T3 #0B2A50, T4 #2D1F52, T5 black #000000, its dominant colour; mean (19, 16, 28)), painted under the image. If the selected image is missing, unreadable, inactive or invalid, the page shows the deployment default T1 (Tile); if even that fails to load, its solid colour. If the logo fails, its alt text "DentalKeep" and the page title remain. In forced-colours mode the browser drops background images; the panel stays readable.

### 2.2 Dependency inventory (new; required by correction 1)
| Package | Version (pinned) | Licence | Advisories (GitHub, 2026-11-12) | Role |
|---|---|---|---|---|
| `SkiaSharp` | 4.156.0 | MIT | 1 (GHSA-j7hp-h8jx-5ppr, libwebp heap overflow, fixed in 2.88.6; 4.156.0 is not affected) | decode PNG/JPEG, inspect frames, re-encode |
| `SkiaSharp.NativeAssets.Win32` | 4.156.0 (transitive) | MIT | none listed | native Skia for the Windows deployment (ALV-N013) |

**Why SkiaSharp:** MIT licence, maintained by Microsoft/.NET Foundation, the same Skia engine as Chrome (heavily fuzzed), and `SKCodec` reads dimensions and frame count from the header without decoding the pixels (needed for correction 2). **Rejected:** `SixLabors.ImageSharp` 4.1.3 (Six Labors Split License: commercial use above a revenue threshold needs a paid licence; 15 advisories), `Magick.NET` 15.1.0 (about 100 advisories, very large native decoder surface), `NetVips` 3.2.0 (MIT wrapper, but the native libvips is LGPL with many optional decoders).

**At implementation:** add only these packages with exact versions; record `dotnet list package --include-transitive` and `dotnet list package --vulnerable` output in the handoff; no floating versions. Decoding is restricted to PNG and JPEG by our own content check before Skia sees the bytes (Skia's WebP/GIF decoders are never reached).

### 2.3 Tile-readiness gate (review R-2, correction 4)
Edge measurements only flag a possible seam; they do not prove a tile is acceptable. Before any bundled asset is packaged, every one of the five bundled backgrounds (T1-T5, not only T3 and T5) passes this gate, and the result is recorded in the handoff:
1. Prepare the bundled derivative (section 2.1, "Bundled form").
2. Render it in a real browser on the sign-in page in **Tile** mode at three representative viewports: 1920 x 1080, 1280 x 800 and 375 x 812, light app theme; capture full-page screenshots (with the owner's explicit permission for the run, section 8).
3. Inspect the repeat boundaries in each screenshot (the screenshots, the boundary positions and the verdict are stored with the evidence).
4. Verdict per asset: **Tile ready** (no objectionable seam), **Reprocessed** (a seam was fixed by cropping or re-tiling the repeat unit, then steps 2-3 repeated on the new derivative), or **Tile not offered** (the asset keeps Stretch and Fill only; the catalog records `tileAllowed = false` for it and the server refuses Tile for it).
5. The default T1 must end **Tile ready**; if it cannot, the owner is asked before packaging.
Until the gate passes, no bundled asset is committed. The catalog carries `tileAllowed` per bundled image; added images allow all three modes (the administrator sees a live preview before saving).

## 3. Current state (repository at `6a7d8c0`)
- `LoginPage.tsx` / `LoginPage.css`: `PageHeader` "Sign in" with "Enter your username and password to access DentalKeep.", status banners, the form. No logo, background or footer. `MfaChallengePage.tsx` and `ResetPasswordPage.tsx` are the other sign-in-flow pages.
- `App.tsx` `RootLayout`: sign-in variant of `DisconnectedBanner` on those pages; the saved app theme applies there (ALV-N020).
- Permissions are role-based: `PermissionMatrix` defaults, `RolePermissionOverride` rows (ALV-N017), and **the Admin role always holds every permission** (`Set(Enum.GetValues<Permission>())`). There is no per-account grant today.
- Configuration: `PracticeSettings` singleton with `RowVersion`, `[RequirePermission]` + `[RequireCsrfToken]`, audit via `ConfigurationWrite.Audit`.
- Files: `DocumentFileRules.DetectContent` recognizes PNG and JPEG by magic bytes; blob storage under `StorageRoot`; `DocumentsController` serves PNG/JPEG inline under `Content-Security-Policy: sandbox`.
- Backup: `ManagedAssetClasses` = database, documents, Data Protection keys, deployment configuration. The code requires a new on-disk store to register an `IBackupAssetSource` and its class name.
- Chain stage 0 asserts the DentalKeep brand on the sign-in page and the shell (B-01, B-02); stage 1 covers sign-in, refusals, expiry, revocation and the outage case.

## 4. Design

### 4.1 Permission `ManageLoginPresentation` (correction 7)
- A new **account-scoped** permission: it is not in any role's defaults, cannot be granted to a role by a policy override (the override writer refuses it), and is **excluded from the Admin role's "every permission"** set. It is held by named accounts listed in a new table `AccountPermissionGrants` (account id, permission, granted by, granted at; unique per account and permission).
- **Initial assignment (owner decision O-9, review R-2):**
  - **New deployment:** the first administrator account created by the existing one-time administrator bootstrap receives the permission **in the same transaction**, audited `LoginPresentationPermissionGranted` (reason "initial holder: first administrator created by the installation bootstrap"). The installer is told explicitly: the bootstrap response and the installer's completion step state "This administrator account is also the sign-in presentation administrator (ManageLoginPresentation)." The ALV-N013 installation documentation records it.
  - **Existing installation (such as the owner's):** nothing is granted automatically. The permission stays unassigned until a deployment operator explicitly selects a named, enabled account through the ordinary audited path (Security administration -> the account -> "Presentation administrator", by a `ManageUsers` holder). The system never infers the holder from creation time, display name, username or role. On the owner's installation the owner's instruction is to grant it to the owner's `admin` account; that grant is made through this path after implementation, by the operator, and audited.
  - Until a holder exists, the sign-in page keeps working with the deployment default background and notice; nobody can change them.
  - It is not a hidden account, preset secret, recovery bypass or super-admin: the holder is an ordinary named account that signs in with its own password (and MFA if enrolled).
- **Transfer:** through Security administration -> the account's detail page -> "Presentation administrator" grant/revoke, by an account holding `ManageUsers` (the ordinary named-account authorization path), CSRF-protected, audited `LoginPresentationPermissionGranted` / `...Revoked` with actor, target and time. A grant to a disabled or Unassigned account is refused. Revoking the last holder is allowed (presentation simply becomes unmanageable until someone is granted it; the sign-in page keeps working).
- **Checks:** every presentation operation requires this permission (account-scoped check by the signed-in account id) and CSRF. `/api/auth/permissions` reports it for the holder, so the client shows the Sign-in screen tab only to holders.
- **Compatibility:** tests and UI that assume "Admin has every permission" are updated to "every role-assignable permission"; the permission matrix page does not list account-scoped permissions as a role column (it notes them separately).

### 4.2 Upload pipeline (corrections 1 and 2)
Order of checks; any failure refuses with a specific code, saves nothing and leaves no temporary file:
1. Permission and CSRF; request body at most 5 MB + form overhead (request-size limit on this endpoint only).
2. Display name: plain text, 1-60 characters, no control characters, no `<` or `>`.
3. Content detection by magic bytes (`DocumentFileRules.DetectContent`): PNG or JPEG only; the extension must agree. SVG, GIF, WebP, BMP, TIFF and everything else: `background_type_refused`.
4. Header inspection with `SKCodec` (no pixel decode yet): width and height each 256-4,096 px, total pixels at most 16,777,216 (4,096 x 4,096); frame count exactly 1 (an animated PNG is refused: `background_animated_refused`); malformed or truncated header: `background_unreadable`.
5. Decode to an RGBA bitmap of exactly the header's size (allocation bounded by step 4), with a decode timeout; any decoder error: `background_unreadable`.
6. Re-encode a **server-generated derivative**: PNG input -> PNG, JPEG input -> JPEG quality 90; colour converted to sRGB; the derivative is a fresh encode of the decoded pixels only, with no metadata copied from the input (no EXIF, XMP, IPTC, ICC beyond sRGB, or PNG text chunks). This is not assumed from the library: the section 8 tests parse every derivative back and fail on any metadata block. If the long side exceeds 2,560 px it is scaled down proportionally for weight.
7. Hash the derivative (SHA-256). The same hash already in the catalog (active or inactive): `background_duplicate`.
8. Active catalog limit: at most 30 active added images: `background_catalog_full`.
9. Store the derivative under a server-controlled name (`<new guid>.png|.jpg`) in a dedicated folder `StorageRoot/login-backgrounds/`, then insert the catalog row in a transaction; if the insert fails, the file is removed. **The original upload is never written to disk and never kept** (processed from a bounded memory buffer).
10. Audit `LoginBackgroundAdded`: image id, display name, derivative hash, type, size, dimensions, actor. Never the bytes, never the user-supplied file name.

### 4.3 Catalog, selection and inactivation (correction 3)
- Catalog entries: the five bundled images (fixed keys `teal-doodle`, `blue-doodle`, `blue-operatory`, `violet-operatory`, `neon-doodle`; cannot be inactivated; each with `tileAllowed` from the gate in 2.3, and Tile is refused for an image without it) and added images (id, display name, hash, type, dimensions, suggested display mode, added by/at, active, row version).
- Selection (singleton `LoginPresentationSettings`): background reference (bundled key or added-image id), display mode (Tile / Stretch / Fill), notice text, show-notice flag, row version. Seeded: `teal-doodle`, Tile, no custom text, show = true.
- **Inactivating the selected image is refused** (409 `background_in_use`: "Choose another background first"). Inactivation and selection run under the settings row's concurrency token in one transaction, so a concurrent change cannot leave the selection pointing at an inactive image.
- Added images are inactivated and reactivated, never deleted (a database trigger refuses deletes, as elsewhere in the system).
- **Safe fallback at read time:** the presentation endpoint resolves the selection; if the image is inactive, missing from storage, unreadable, or the reference is unknown, it answers with the deployment default `teal-doodle` / Tile (and logs `login_background_fallback` with the reason, once per change).

### 4.4 Anonymous endpoints (correction 4)
- `GET /api/public/login-presentation` -> `{ background: { kind: "bundled", key } | { kind: "added", url: "/api/public/login-background/<sha256>.<png|jpg>" }, displayMode, footerNotice: string | null }`; `Cache-Control: no-store`. `footerNotice` is null when display is off or the effective text is empty (a disabled notice is not sent).
- `GET /api/public/login-background/{sha256}.{ext}` serves the derivative **only if that hash is the currently selected active added image**; otherwise 404 (no catalog, no listing, no other id, no inactive image, no original). Headers: fixed `Content-Type` (`image/png` or `image/jpeg`), `X-Content-Type-Options: nosniff`, `Cross-Origin-Resource-Policy: same-origin`, `Content-Security-Policy: sandbox`, `Content-Disposition: inline; filename="login-background.<ext>"` (server-controlled), `Cache-Control: public, max-age=31536000, immutable` (content-addressed, so safe to cache). No storage path or user file name in any response.
- Both endpoints are read-only, identical for every visitor, and rate-limited by a modest per-IP policy of their own; the image is read through a bounded stream.

### 4.5 Display modes
| Mode | CSS on the page background | UI note |
|---|---|---|
| Tile | `background-repeat: repeat; background-size: auto` (natural size) | - |
| Stretch | `background-size: 100% 100%; background-repeat: no-repeat` | "May distort the image." |
| Fill | `background-size: cover; background-position: center; background-repeat: no-repeat` | "Keeps proportions; edges may be cropped. Recommended for photographs." |
The logo is never stretched (`object-fit: contain`, fixed aspect ratio).

### 4.6 Footer notice
- Deployment default from `LoginPresentation:DefaultFooterNotice` (deployer-editable during or after deployment; validated at start-up with the same rules; an invalid default is logged and treated as empty). The shipped USA default (O-4), two paragraphs:
  > Please ensure you have taken appropriate precautions to guard against inappropriate sharing of Protected Health Information (PHI) pursuant to HIPAA, 42 CFR Part 2, and all applicable state laws regarding confidential patient information. Do not share your password with anyone.
  >
  > All access to this system will be tracked and monitored by each organization. Inappropriate access may result in reporting to organizational management or law enforcement officials. By utilizing this system, you express consent to monitoring and abiding by federal and state regulatory requirements for patient/consumer information.
- Custom text overrides it; "Use deployment default" clears the override. Rules: trimmed; 1-1,000 characters; at most 10 line breaks; no other control characters or tabs; `<`, `>` and URL forms (`://`, `www.`, `mailto:`) refused. Rendered as a React text node with `white-space: pre-line`; never `dangerouslySetInnerHTML`, never auto-linked. No country detection.

### 4.7 Upload warning (correction 5)
Shown prominently above the Add background image control and in its dialog, and the dialog requires ticking it: "This image is public. Anyone who opens the sign-in page sees it before signing in. Never use an image that contains patient information, faces, clinical documents, screenshots of this application or of patient data, or any other protected or confidential information." The warning is guidance, not a control; the controls are the narrow permission, the audit trail, and inactivation.

### 4.8 Backup and restore (correction 6)
New managed asset class `loginBackgrounds` registered with an `IBackupAssetSource` over `StorageRoot/login-backgrounds/` (file-by-file SHA-256, like documents). A new restore check `restored_login_backgrounds_have_their_files`: every active added image row has its derivative with the matching hash. A backup that predates the class is reported incomplete for it (existing behavior for new classes).

### 4.9 Client
- `LoginLayout` used by `LoginPage`, `MfaChallengePage` and `ResetPasswordPage`: page background (decorative CSS background, solid fallback colour underneath), full-height light centre panel (owner mockup), logo `<img alt="DentalKeep">`, the existing content with visible labels **Username** / **Password** and the **Sign in** button (R-1), the footer `<footer>` only when `footerNotice` is a non-empty string.
- The presentation fetch never delays sign-in: the page renders at once with the default background and no footer; the fetched values apply when they arrive; a failed fetch keeps the default and shows no footer; the "Local server unavailable" sign-in banner is unchanged.
- The panel uses the existing light/dark app tokens; narrow widths: the panel spans the viewport minus the 16 px gutter, the logo scales with its aspect ratio, no horizontal scroll; no animation.
- Practice configuration -> **Sign-in screen** tab (holders only): catalog grid with thumbnails (radio group, keyboard-operable, labelled), display-mode radio group with the notes, Add background image (file input, name, warning acknowledgement), Make unavailable / Make available, notice text box with character count and rule messages, Show footer notice on login screen, Use deployment default, Save with the existing unsaved-changes guard and conflict message.

### 4.10 Audit events (all via the audit service: actor and time)
`LoginPresentationPermissionGranted`, `LoginPresentationPermissionRevoked`, `LoginBackgroundAdded`, `LoginBackgroundInactivated`, `LoginBackgroundReactivated`, `LoginBackgroundSelected` (previous and new reference, previous and new display mode), `LoginFooterNoticeChanged` (display state before/after, custom/default, character count; not the full text), `LoginFooterNoticeReset`. Never credentials, image bytes or user-supplied file names.

## 5. Threat surface (new)
| Surface | Threat | Control |
|---|---|---|
| Upload endpoint (authenticated, privileged) | malicious or malformed files, decoder exploits, decompression bombs | narrow permission + CSRF; 5 MB limit; magic-byte type check before decoding; header dimension, total-pixel and frame checks before decoding; pinned, advisory-checked decoder; decode only PNG/JPEG; bounded decode; derivative re-encode |
| Upload content | metadata leaks (EXIF/XMP/IPTC/PNG text chunks, GPS), hidden payloads | only the re-encoded pixel data is stored and served; the original is never kept |
| Upload content | PHI or faces shown publicly | permission limited to one designated account, prominent warning with acknowledgement, audit, inactivation |
| Anonymous presentation endpoint | enumeration, data exposure, load | two values, identical for everyone, `no-store`, rate-limited, no identifiers beyond the selected image's content hash |
| Anonymous image endpoint | catalog enumeration, serving inactive/other images, MIME confusion, cross-origin use | serves only the current selection's hash, 404 otherwise; fixed type, `nosniff`, CORP same-origin, CSP sandbox, server-controlled name |
| Footer notice | stored XSS, phishing links | plain-text storage and rendering, markup and URL refusal, length/control-character limits |
| Permission model | privilege creep (Admin inherits everything) | account-scoped permission excluded from Admin defaults and from role overrides; grants audited |
| Storage | path traversal, overwrite | server-generated names only, dedicated folder, no client-supplied path ever used |
| Availability | a bad image breaks sign-in | fallback to the bundled default; sign-in never waits for presentation data |

## 6. Migration and compatibility
- **One migration**: tables `LoginPresentationSettings` (singleton, seeded), `LoginBackgroundImages` (no-delete trigger, unique hash), `AccountPermissionGrants` (unique account+permission). Down on empty/seeded tables drops them; down with added images or grants is refused (immutable history), stated and tested.
- New enum value `Permission.ManageLoginPresentation`, excluded from role sets; permission-matrix and policy code updated so it is never role-assignable; existing role behavior otherwise unchanged.
- New backup asset class `loginBackgrounds` (older backups report it missing, as designed).
- New deployment setting `LoginPresentation:DefaultFooterNotice` (documented for the ALV-N013 installer). The initial permission holder needs no setting (first administrator, section 4.1).
- New package `SkiaSharp` 4.156.0 (section 2.2).
- No change to `AuthController` sign-in, `AccountService`, `AuthContext`, `RouteGuards`, CSRF, lockout or session code.

## 7. Permission, audit, data, backup, recovery, accessibility implications
| Area | Implication |
|---|---|
| Permission | new account-scoped `ManageLoginPresentation`; grant/revoke by `ManageUsers` holders; initial holder = the first administrator created by the bootstrap on a new deployment (explicit to the installer, audited); on an existing installation unassigned until an operator grants it to a named account through the audited path |
| Audit | events in 4.10; no bytes, names of uploaded files or credentials |
| Data | derivatives on disk under server names; catalog and settings rows; no PHI expected (warning + audit) |
| Backup | new asset class and restore check (4.8) |
| Recovery | missing/unreadable derivative -> default background; restore re-checks files; lost holder -> a `ManageUsers` administrator grants the permission to another named account (audited) |
| Accessibility | solid panel, visible labels, contrast per background in light and dark app themes, keyboard-only sign-in and configuration, 200 % zoom, forced colours, Stretch/Fill notes as text |

## 8. Focused acceptance evidence (implementation phase)

**Run permission:** every executable test group (unit, API, browser, the tile-readiness screenshots, the chain and the walkthrough) requires the product owner's explicit permission before it starts, regardless of its estimated duration, unless the owner adopts a separate written standing rule. The chain and walkthrough evidence below is not run before that permission.
| Requirement | Tests |
|---|---|
| Initial owner-only assignment | bootstrap grants it to the first administrator in the same transaction and audits it; the bootstrap response states it; a second Admin created later does not get it; not inherited by the Admin role; an existing database (migrated, no bootstrap) has **no** holder after start-up, and an operator grant through the audited path then works; restarting never grants anything |
| Authorization / transfer / CSRF | holder 200; Admin without grant 403; FrontDesk etc. 403; anonymous 401; missing CSRF refused; `ManageUsers` grant/revoke audited; grant to disabled/Unassigned refused; role override for the permission refused |
| Type refusal by content | SVG, GIF, WebP, BMP, text renamed `.png`, PNG renamed `.jpg` (mismatch) |
| Malformed / unsupported / animated | truncated PNG, corrupt JPEG, APNG with 2 frames |
| Size / dimensions / total pixels / duplicates | 5 MB + 1 byte; 255 px and 4,097 px sides; 4,096 x 4,097; the same image twice (and once after inactivation) |
| Metadata stripping | JPEG with EXIF GPS, XMP and IPTC; PNG with tEXt/iTXt/eXIf chunks -> the stored derivative has none (parsed back) |
| Derivative-only storage | only one file per image under a server name; no original on disk after success or failure; temp-file sweep |
| Public endpoint boundary and headers | presentation payload shape and `no-store`; image endpoint serves only the selected hash, 404 for an inactive, other, unknown or original reference; exact headers |
| Selection / inactivation | inactivating the selected image -> 409; concurrent select/inactivate -> one wins, never an inactive selection; missing file / inactive / unknown reference -> default |
| Display modes | each mode renders the expected CSS (unit) and in a browser screenshot |
| Concurrency and audit history | stale settings write 409, nothing changed; every event in 4.10 with the right fields and no forbidden content |
| Footer | enabled/disabled rendering (absent from DOM and payload when off); `<script>`/`<img onerror>` shown literally or refused; 1,000/1,001 characters; 10/11 line breaks; deployment default and reset |
| Sign-in page | keyboard-only sign-in; axe per bundled background in light and dark themes at 1280 px and 375 px; 200 % zoom; forced colours; sign-in not delayed by a slow or failed presentation fetch |
| Unchanged security | chain stage 0 + stage 1 re-run (B-01/B-02, 1.01-1.12, F-021, F-037, 13.05) and the `auth` walkthrough |
| Backup / restore | an actual backup and restore of an added derivative: the file returns with the same hash and the restore check passes; a missing file fails the check |
| Schema | migration up/down on a clone; no-delete trigger; unique hash; registered in `RealMigrationInventory` |
| Negative controls | markup rendered as HTML; disabled notice still sent; permission check removed; metadata copied through; non-selected image served |

## 9. Risks
| Risk | Mitigation |
|---|---|
| Native decoder vulnerability | pinned SkiaSharp, advisory check recorded, type and header checks before decoding, re-encode, upload limited to one designated account |
| An unsuitable public image | warning with acknowledgement, audit, quick inactivation |
| Account-scoped permission breaks "Admin has everything" assumptions | explicit exclusion, matrix/policy tests updated, architecture test that no role set contains it |
| Heavier sign-in page | bundled derivatives under 250 KB; added images capped at 2,560 px long side; sign-in never waits |
| Logo artefacts (L2) | resolved: the owner approved the cleaned copy L3 |
| Larger story | the focused test set above; broad regression stays with ALV-N028 |

## 10. Open items
| # | Item | Who |
|---|---|---|
| Q-1 | Resolved (O-8, O-10): L3, the cleaned copy, is the approved logo | - |
| Q-2 | Resolved (O-9, R-2): new deployment = the bootstrap's first administrator; existing installation = explicit operator grant (owner: `admin`) | - |
| Q-3 | Transfer path: grant/revoke by `ManageUsers` holders (proposed) | reviewer |
| Q-4 | Inactivating the selected image: refuse (proposed) rather than atomically reset to the default | reviewer |
| Q-5 | Limits: 30 active added images, 2,560 px derivative cap, 10 line breaks in the notice (proposed) | reviewer |

## 11. Method and limits
Read-only: the amended plan, the scope-change request and review, the current login, permission, configuration, document-storage and backup code, the image files (dimensions, alpha, edge seams, SHA-256 with Python/PIL; images viewed), and public package metadata (NuGet specifications for licences and versions; the GitHub advisory database for advisories). No file was copied into the repository; no code, schema, configuration, asset, dependency or test was changed; no test was run.
