# ALV-N020-C01 - Scope change request: administrator-added sign-in background images

- **Requested by:** the product owner (2026-11-12)
- **Prepared by:** coding agent, session CC-20261109-8w3d
- **Concerns:** plan item 57a `ALV-N020-C01` (and its effect on the deferred `ALV-N029`)
- **Status:** REQUEST for independent review. Nothing is implemented; the pre-implementation briefing (`docs/design/ALV-N020-C01_PRE_IMPLEMENTATION_BRIEF.md`) will be revised after this decision.

## 1. What the owner asks for (owner's words, condensed)

> "The admin can click 'Add background image', select the image from the desk[top], add it to the collection, and specify the display mode: tile, stretch, fill."

New background images must be addable without a code change or rebuild. The owner has already added a fifth image and expects to add more.

## 2. Why this needs approval

The approved plan item 57a says:
- Acceptance: "The theme catalog is a **fixed allow-list of bundled asset identifiers**, with a safe visual fallback if an asset is unavailable."
- Acceptance: "...without stretching, remote retrieval, tracking, or **user-uploaded assets**."
- Explicit exclusions: "Do not add ... external/remote asset URLs, **user-uploaded tiles**, ..."

The request replaces the fixed bundled catalog with a managed catalog that an authorized administrator can add to, and adds "stretch" and "fill" display modes. It touches the security posture (a new file-upload path whose result is shown to unauthenticated visitors), so it is a governance decision, not an implementation detail.

## 3. Owner decisions already given for this item (2026-11-12)
| # | Decision |
|---|---|
| D-1 | Keep the current logo artwork (it reads "KEEP dental"); use the logo the owner provided. |
| D-2 | The owner holds the rights to all supplied images; use them. |
| D-5 | Default footer notice (USA), two paragraphs, 610 characters: "Please ensure you have taken appropriate precautions to guard against inappropriate sharing of Protected Health Information (PHI) pursuant to HIPAA, 42 CFR Part 2, and all applicable state laws regarding confidential patient information. Do not share your password with anyone." / "All access to this system will be tracked and monitored by each organization. Inappropriate access may result in reporting to organizational management or law enforcement officials. By utilizing this system, you express consent to monitoring and abiding by federal and state regulatory requirements for patient/consumer information." The briefing's 300-character limit becomes 1,000. |
| Mockup | The owner's mockup: the background fills the page; a full-height light centre panel holds the logo, the two fields, the button and, at the bottom, the footer notice. |

Still open with the owner: the default background (the mockup uses the teal doodle tile) and the field/button wording ("User ID" / "Log In" in the mockup versus today's "Username" / "Sign in"; visible labels are needed for accessibility either way).

## 4. Proposed design (what would be built if approved)

### 4.1 Catalog
- **Built-in images:** the five owner-supplied images are bundled with the application (same origin), cannot be deleted, and remain available even with an empty database. One of them is the deployment default.
- **Added images:** an administrator adds an image from their computer. It is stored by the server (the existing blob storage under `StorageRoot`) with a database row: id, display name, file hash, detected type, size, width, height, default display mode, added by, added at, active flag. Images are **inactivated, never deleted** (history and audit kept), consistent with the rest of the system.
- **Allow-list, kept in a new form:** only built-in ids and active added-image ids can be selected; anything else is refused by the server and shown as the default. Nothing outside the catalog (no URL, no path) can ever be configured.

### 4.2 Display modes
| Mode | Meaning | CSS |
|---|---|---|
| Tile | repeat at natural size | `background-repeat: repeat; background-size: auto` |
| Stretch | fill the page exactly; may distort | `background-size: 100% 100%; no-repeat` |
| Fill | cover the page, keep proportions, crop edges | `background-size: cover; background-position: center; no-repeat` |

The mode is stored with the active selection (an added image also has a suggested default mode). "Stretch" is offered because the owner asked for it; the briefing will note that it distorts non-matching images and recommend "Fill" for photographs.

### 4.3 Upload safeguards (reusing the document-import rules where they exist)
| Rule | Detail |
|---|---|
| Who | Existing practice-configuration authority (`ManagePracticeConfiguration`), CSRF token required; everyone else 403 |
| Types | PNG and JPEG only, detected from the file's content (`DocumentFileRules.DetectContent`), not the name; the extension must agree. **No SVG** (it can carry script), no GIF, no WebP initially (no content check exists for it yet) |
| Size | at most 5 MB per file; dimensions read from the image header: 256 to 4,096 px per side; refused otherwise |
| Count | at most 30 active added images |
| Duplicates | the same file (same hash) is refused as already in the collection |
| Name | display name required, plain text, 1-60 characters |
| Serving | from the same origin only, with the stored content type, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: sandbox`, never as a download of a user-supplied name; the sign-in page fetches the active image through an anonymous endpoint that serves **only the currently selected image** (not the whole collection), long-cached by hash |
| Privacy | the image is shown to anyone who opens the sign-in page, before sign-in. The upload screen states: "Shown to everyone before sign-in. Never use an image with patient information or faces." **Known limit:** without an image-processing library the server cannot strip embedded metadata (for example GPS data in a phone photo's EXIF). Options: (a) refuse JPEG files that carry EXIF location data (header check only), (b) add an image-processing dependency to re-encode every upload (a new external dependency needs its own approval), (c) accept and warn. **Proposed: (a).** |
| Audit | image added (id, name, hash, type, size, dimensions, actor), image inactivated/reactivated, selection changed (old and new id and mode). No image bytes in the audit |
| Backup | added images live in `StorageRoot`, so they are part of the existing backup set; to be confirmed and tested in the implementation |
| Failure | an unreadable or missing stored file falls back to the default built-in image; the sign-in page never waits for the background |

### 4.4 What does not change
Authentication, account-enumeration resistance, lockout, CSRF, session expiry/revocation, sign-out, the ALV-N020 outage behavior, the solid centre panel (the background never sits behind text), plain-text-only footer notice, no remote URLs, no tracking.

### 4.5 Persistence
Two tables instead of the one in the briefing: the sign-in presentation singleton (selected image id, display mode, notice text, show-notice flag, row version) and the added-images catalog. One migration. Added files in the existing blob store.

### 4.6 Effect on ALV-N029 (deferred rotation)
Rotation would cycle through the whole active catalog (built-in and added images), each with its stored mode. ALV-N029's text ("which already-approved bundled theme is active") would need the same wording change when it is started; no change to it now.

## 5. Plan text changes requested (item 57a)
1. Scope: "Bundle a curated catalog of local, repeatable background-tile assets" -> "Bundle a curated catalog of local background images and let an authorized administrator add images to it, each shown in a chosen display mode (tile, stretch or fill)."
2. Acceptance: "The theme catalog is a fixed allow-list of bundled asset identifiers" -> "The background catalog is an allow-list of bundled images and administrator-added images that passed the upload safeguards (content-detected PNG/JPEG, size and dimension limits, no SVG), stored and served from the same origin".
3. Acceptance: "...without stretching, remote retrieval, tracking, or user-uploaded assets" -> "...without remote retrieval, tracking, or uploads by anyone but an authorized administrator; the logo is never stretched".
4. Acceptance (new): "Adding, inactivating and selecting images, and the display mode, are authorized, validated, concurrency-safe and audited; added images are inactivated, never deleted; the sign-in page serves only the selected image."
5. Exclusions: remove "user-uploaded tiles"; add "SVG or other scriptable image formats; images from URLs; image upload by anyone without configuration authority".
6. Required coverage (add): upload refusals (wrong type by content, SVG, oversize, too large/small dimensions, duplicate, EXIF location), served headers, anonymous endpoint serves only the selected image, inactivated image falls back, each display mode rendered, backup includes added images.

## 6. Cost and risk of the change
- Larger than the approved item: one more table, an upload endpoint, an anonymous image endpoint, file validation and storage, a catalog management screen; about one extra day of implementation and a larger focused test set (still within focused evidence; broad regression stays deferred to ALV-N028).
- New attack surface: a file upload whose result is shown before sign-in. Mitigated by the safeguards above; the reviewer may require more (for example re-encoding, which needs a new dependency decision).
- Privacy: an administrator could upload an unsuitable image; mitigated by the warning, the audit trail and inactivation.

## 7. Decision requested
| Option | Result |
|---|---|
| **A. Approve the change inside ALV-N020-C01 (owner's request)** | The briefing is revised to this design; the plan text in section 5 is amended by the owner |
| B. Keep ALV-N020-C01 as approved (bundled catalog only) and add a separate companion item for administrator-added images | Two smaller items |
| C. Refuse administrator uploads | The bundled catalog only; new images need a code change and rebuild |

The reviewer is also asked to rule on the EXIF option in 4.3 (proposed (a)).
