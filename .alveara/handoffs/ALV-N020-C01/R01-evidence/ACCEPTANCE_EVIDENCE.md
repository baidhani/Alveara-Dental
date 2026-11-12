# ALV-N020-C01 R01: acceptance evidence

Plan item 57a, the 16 sentences of its Acceptance paragraph (as amended for the managed catalog), each with its proof.

| # | Acceptance sentence (condensed) | Met? | Evidence |
|---|---|---|---|
| 1 | Local background and the approved logo, no remote retrieval or tracking | Yes | Bundled assets served by the build (same origin); added images only from `api/public/login-background/...` (LoginLayout refuses any other URL: `LoginLayout.test` "an added URL that is not our image route"); no third-party requests in the layout |
| 2 | Usable at desktop and narrow widths; form, focus, error, loading, server-unavailable states meet contrast/keyboard; background cannot impair reading | Yes, except 200 % zoom / forced colours not tested (limitation 1) | `login-presentation.spec.ts`: axe (all rules) for every background, light and dark, 1280 and 375 px, no horizontal scroll; keyboard order Username -> Password -> Sign in; solid panel (background never behind text); existing `login-banner-contrast.spec.ts` (expired and failure banners, both themes) 28/28 with shell; chain sign-in/expiry/outage steps 20/20 |
| 3 | Tile natural size; Stretch may distort and says so; Fill keeps proportions | Yes | `backgroundCss` unit test; browser test (computed `background-size` per mode, screenshots); tab notes ("May distort the image", "Recommended for photographs") in `SignInScreenTab.test` |
| 4 | Catalog = bundled images + active added server-generated derivatives | Yes | `LoginPresentationApiTests` (select bundled / added; unknown refused `background_unknown`); `LoginBackgroundCatalogTests` (client map = server catalog) |
| 5 | Added images PNG/JPEG only by content; bounded size, dimensions, total pixels; decoded and re-encoded metadata-free; server names; never served from the original | Yes | `LoginBackgroundPipelineTests` 34 (type refusal by content, extension mismatch, 5 MB, 255/256/4,096/4,097 px, scaled to 2,560, truncated, APNG, EXIF/XMP/IPTC and PNG text/eXIf absent from the derivative); API test (one server-named file, no original, served bytes carry no APP1); negative control NC-4 |
| 6 | SVG, GIF, WebP, other formats, URLs, paths, markup, non-authorized uploads, remote content refused | Yes | pipeline tests (SVG, GIF, WebP, BMP, text); API (SVG 415, acknowledgement required, path-like image names 404, `../etc` bundled key refused); notice rules (markup, links); upload needs the account permission |
| 7 | Missing, unreadable, inactive or invalid selection falls back to the bundled default | Yes | API "A selected background whose file is missing falls back"; service fallback for inactive/unknown; client fallbacks (unknown key, bad URL, unknown mode, fetch failure, image load error path) |
| 8 | Anonymous endpoint serves only the selected derivative with server-controlled headers; no listing, original name or path | Yes | API test (headers `nosniff`, `same-origin`, `sandbox`, `login-background.jpg`; other hash 404; deselected 404); public presentation has exactly three fields; NC-5 |
| 9 | Image/notice operations need `ManageLoginPresentation`; initially only the owner-designated account; validated, concurrency-safe, CSRF, audited without credentials/bytes/file names/duplicated notice text | Yes | API tests (bootstrap holder, second Admin 403, FrontDesk/OfficeManager 403, anonymous 401, missing CSRF changes nothing, stale version 409, audit details without the file name or notice text); bootstrap response states it to the installer; NC-3 |
| 10 | Not a hidden account, preset secret, bypass or recovery mechanism | Yes | Design (an ordinary named account's grant; no new account or secret); permission held only through `AccountPermissionGrants` |
| 11 | Later transfer through the ordinary named-account path, audited | Yes | `PUT api/auth/{id}/presentation-administrator` by a ManageUsers holder: grant, idempotent repeat, revoke, disabled refused, FrontDesk 403, audit counts (API test); `PresentationAdministratorControl` test |
| 12 | Added images inactivated, not deleted; the selection cannot point to an unavailable image | Yes | API (`background_in_use`, `background_unavailable`); schema (51276 delete refused, 51277, 51279, 51278 facts immutable); the concurrent race itself is not forced by a test (limitation 2) |
| 13 | Upload warning: public before sign-in; never patient information, faces, clinical documents, screenshots, confidential information | Yes | `PUBLIC_IMAGE_WARNING` shown in the tab and the form, acknowledgement required (client test) and enforced by the server (`public_acknowledgement_required`) |
| 14 | Footer text plain text only; length/whitespace validation | Yes | `LoginNoticeRulesTests` (1,000/1,001, 10/11 line breaks, tab, control, `<`, `>`, links); `LoginLayout.test` and browser test (markup shown literally, no element created); NC-1 |
| 15 | Default notice deployer-editable; disabled = not rendered, not merely hidden | Yes | `appsettings.json` `LoginPresentation:DefaultFooterNotice` (the owner's USA text; test that it passes the rules); API (disabled -> `footerNotice: null`); layout/browser tests (no `footer` element); NC-2 |
| 16 | Authentication, enumeration resistance, lockout, CSRF, expiry/revocation, sign-out and N020 outage behavior unchanged | Yes | No change to sign-in, `AccountService` login, `AuthContext` session logic or route guards (only the account-permission merge for UI gating); chain stage 0 + stage 1 20/20 (24 PASS): refusals 1.02, disabled 1.04/1.05, revocation 1.06, Back 1.10, MFA 1.11, outage 13.05, expiry 1.12, brand B-01/B-02; auth walkthrough 12/12; permission tests 103/103 |

Result: **16 of 16 met**; limitations 1 (200 % zoom, forced colours) and 2 (concurrent race) are disclosed in `R01.md`.

## Review requirements from BRIEFING-R03 and the scope-change review
| Requirement | Evidence |
|---|---|
| Initial owner-only assignment; named-account authorization/denial; audited transfer; CSRF denial | API tests listed in rows 9 and 11 |
| Content type, malformed/unsupported/animated, size, dimensions, total pixels, duplicates | rows 5, 6; API duplicate and 31st-image refusal |
| Metadata stripping; derivative-only storage and serving | row 5; NC-4 |
| Public-endpoint boundary and headers | row 8; NC-5 |
| Active-image inactivation and fallback; each display mode | rows 3, 7, 12 |
| Concurrency and audit history | rows 9, 12 |
| Footer enabled/disabled and escaping | rows 14, 15; NC-1, NC-2 |
| Narrow viewport, keyboard, contrast | row 2 |
| Actual backup/restore of an added derivative | `LoginBackgroundBackupRestoreTests` (restored byte for byte with its hash; a missing file fails the restore validation) |
| Tile-readiness gate for all five | `artifacts/focused-proof/tile-gate/TILE_GATE_RESULT.md`: all Tile ready |
| Q-5 boundary tests (30 images, 2,560 px, 10 line breaks) | API 30/31; pipeline 4,096 -> 2,560; notice rules 10/11 |
