# ALV-N020-C01 — Scope Change Review: Administrator-Added Sign-In Background Images

- **Decision date:** 2026-11-12
- **Reviewer decision:** **APPROVED WITH REQUIRED DESIGN CORRECTIONS**
- **Reviewed request:** `docs/design/ALV-N020-C01_SCOPE_CHANGE_REQUEST.md`
- **Implementation authorization:** **Not yet authorized.** Revise the plan and pre-implementation briefing below, then stop for review.

## Decision

Option A is approved: administrator-managed additions to the local sign-in background catalog belong in `ALV-N020-C01`. This meets the product-owner requirement that later backgrounds can be added without a code change or rebuild. It remains a local, server-managed catalog; it does not permit remote image URLs, user-facing uploads, or a new authentication path.

This is no longer a presentation-only change. It creates a privileged file-upload and anonymous image-serving surface, so the following requirements are binding.

## Required design corrections

1. **Replace the proposed EXIF-location-only control.** It is insufficient: it does not address other EXIF/XMP/IPTC/PNG metadata or metadata-bearing image variants. Every administrator-uploaded image must be decoded and re-encoded to a clean server-generated derivative before it is stored or served. The derivative must strip metadata. Select, pin, license-review, and vulnerability-check the image-processing dependency before use; document that decision in the revised briefing. Do not retain or serve the original upload.
2. **Defend against decompression bombs.** Enforce the 5 MB input limit, per-side limits, and an explicit total-pixel limit before/while decoding. Reject malformed, truncated, animated, or unsupported inputs safely. PNG and JPEG only; SVG, GIF, WebP, and every other format remain refused.
3. **Make selection/inactivation atomic.** An image that is the active selection cannot be left active in configuration while being made unavailable. Either refuse its inactivation until another image is selected, or atomically reset the selection to the bundled deployment default in the same transaction. A missing, unreadable, inactive, or otherwise invalid selected image must fall back safely to that default.
4. **Keep the anonymous serving boundary narrow.** The endpoint may expose only the currently selected approved image, not a catalog, list, identifier enumeration route, original upload, storage path, or user-provided filename. Use server-controlled storage names and content disposition; emit fixed content type, `X-Content-Type-Options: nosniff`, and same-origin/cross-origin resource policy headers. Cache only the content-addressed derivative, never a mutable identifier indefinitely.
5. **Do not treat the warning as a privacy control.** The administrative upload interface must prominently state that the image is public before sign-in and must never contain patient information, faces, clinical documents, screenshots containing application/patient data, or other protected/confidential information. Audit the upload and lifecycle events, but never store image bytes or the user-supplied filename in the audit log.
6. **Prove backup/restore.** Added-image derivatives are part of the protected application storage set. Demonstrate backup and restore preservation explicitly; do not infer it solely from `StorageRoot` usage.
7. **Use the narrow presentation permission.** Add `ManageLoginPresentation`; it initially belongs only to the owner-designated named deployment account. It is not a hidden or preset-password “super-admin” account, authentication bypass, or recovery mechanism. Any later transfer uses the ordinary named-account authorization path and is audited. Require this permission and CSRF protection for every presentation-image and footer-notice operation; apply server-side validation, optimistic concurrency, bounded active-catalog count, duplicate hash refusal, plain-text display-name validation, and immutable lifecycle/audit history. Do not allow arbitrary paths, URLs, markup, user uploads by non-authorized users, or remote retrieval.

## Approved product choices

- The catalog may offer **Tile**, **Stretch**, and **Fill**. Label Stretch with a visible note that it may distort the image, and recommend Fill for photographs.
- Use the teal dental-doodle tile in the owner mockup as the initial deployment default unless the product owner changes it in the revised briefing.
- Retain **Username** and **Sign in** as the visible labels. Do not use “User ID” unless the product supports a distinct ID credential.
- The current logo artwork is approved for use as the owner-supplied logo asset.
- The configured login-footer notice remains plain text only. The approved USA default is permitted up to 1,000 characters, editable by an authorized administrator, and omitted entirely when `Show footer notice on login screen` is disabled.

## Plan and briefing revision required

The authoritative execution-plan text for `ALV-N020-C01` and the deferred `ALV-N029` has been amended for this reviewed managed-catalog design. Include that amendment in the document-only control/planning commit. Do not start `ALV-N029` now.

Then revise `docs/design/ALV-N020-C01_PRE_IMPLEMENTATION_BRIEF.md` to include all requirements above, the asset/dependency inventory, migration and compatibility impact, permission/audit/data/backup/recovery/accessibility implications, complete targeted acceptance evidence, and the required raw finding entries. The briefing must explicitly identify the new upload and anonymous-serving threat surface.

Commit the plan and document-only revisions as a control/planning change. Do not modify application code, schema, configuration, assets, tests, dependencies, or execute test runs until the revised briefing receives independent approval.

## Required evidence once implementation is later authorized

At minimum, cover the initial owner-only assignment, named-account authorization/denial, audited permission transfer, and CSRF denial; content-detected type refusal; malformed/unsupported/animated input refusal; size, dimensions, total-pixel and duplicate rejection; metadata stripping; server-generated derivative-only storage/serving; public-endpoint boundary and headers; active-image inactivation behavior; fallback; each display mode; configuration concurrency and audit history; footer enabled/disabled and escaping; narrow-viewport/keyboard/contrast behavior; and an actual backup/restore of an added background derivative.

Broad regression remains deferred to `ALV-N028` under the stabilization-program exception. That does not waive focused API, persistence, security, browser, accessibility, and recovery evidence for this item.
