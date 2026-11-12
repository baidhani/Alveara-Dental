# ALV-N020-C01 Pre-Implementation Briefing

**DentalKeep Login Presentation, Curated Manual Theme, and Configurable Footer Notice**

- **Plan item:** 57a (`GPT Docs/Alveara_Dental_Execution_Plan_v4.md`, owner-approved amendment of 2026-11-12, committed in `db138ec`)
- **Depends on:** ALV-N020 (COMPLETE, R02 approved, closure `4ada83a`)
- **Session:** CC-20261109-8w3d
- **Status of this document:** briefing only. No application code, test, configuration, schema or asset has been changed, and no test has been run.
- **Regression route:** post-Gate-E deferred-regression exception: focused evidence for this package; broad regression `DEFERRED_TO_ALV-N028`.
- **Out of scope here:** automatic theme rotation (`ALV-N029`, deferred until after `ALV-N027`); the configuration-tab busy-state finding F-075 (stays with `ALV-N027`).

## 1. Intended result

### 1.1 What a person sees
- The sign-in page shows the DentalKeep logo above a clean sign-in card, over a softly repeating dental-illustration background. The card is solid (not see-through), so the background never affects reading or typing.
- The page works at desktop and phone widths, by keyboard alone, at 200 % zoom, in light and dark app themes, and in Windows high-contrast mode. Error, loading and "Local server unavailable" states look and read as they do now.
- Below the card, an optional footer notice (for example a legal or authorized-use notice) is shown as plain text. When it is turned off it is not on the page at all.
- An administrator (anyone with the existing practice-configuration authority) picks one of four approved backgrounds and edits the footer notice in Practice configuration, sees who changed what in the audit log, and can return the notice to the deployment default.

### 1.2 Technical
- A fixed, bundled theme catalog (four allow-listed identifiers); no remote images, no uploads, no tracking.
- One small persisted singleton for the chosen theme and the footer notice, with optimistic concurrency and audit; one anonymous read endpoint that returns only what the sign-in page needs.
- No change to authentication, CSRF, lockout, account-enumeration resistance, session expiry/revocation, sign-out, or the ALV-N020 outage behavior.

## 2. Asset inventory (owner-supplied; read in place, not yet copied into the repository)

All five files are in the owner's folder `C:\Users\firas\Desktop\Epic\`. Nothing has been copied, converted or committed. Hashes and dimensions were read on 2026-11-12.

| # | File | Role (proposed) | Pixels | Size | SHA-256 | What it shows |
|---|---|---|---|---|---|---|
| L1 | `dental-keep-v01.png` | Logo | 1774 x 887, RGBA, fully opaque (alpha 255 everywhere) on a light grey #F7F6F6 background; ink area about x 149-1627, y 156-790 | 423,886 B | `acfaecd34fb9558f1ccb87a6dec42bc6d2593dd370071fc301ca7e94d86e3638` | Teal "K", two tooth outlines forming "EE", teal "P"; coral "dental" below. Reads **"KEEP dental"** |
| T1 | `ChatGPT Image Nov 10, 2026, 10_41_23 AM.png` | Tile "teal doodle" | 1254 x 1254, RGB | 1,676,595 B | `5dd15d6b763f67e9946bd722d7280fdbd99023f7f0ad4e62580fd501a9627699` | Teal line doodles (smiling teeth, toothbrush, dental chair, braces, floss) on near-black teal; mean colour (4, 43, 48) |
| T2 | `ChatGPT Image Nov 10, 2026, 04_00_35 PM.png` | Tile "blue doodle" | 1254 x 1254, RGB | 1,946,848 B | `78f9a1f8b53b609e3adc25dd3fd2cd9bb1fe644cdfee98a360d4ddf6de1ddd43` | Light-blue doodles (teeth, balloons, clouds, sun) on navy; mean (13, 49, 78) |
| T3 | `ChatGPT Image Nov 10, 2026, 04_01_00 PM.png` | Tile "blue operatory" | 1254 x 1254, RGB | 1,527,257 B | `33084e9d07bc05721b18036ad1e59d6641a24a81e19eb8caa7e57879e6c1bace` | Blue line art of a dental operatory (chair, light, X-ray screen, clinician, sink) on navy; mean (11, 42, 80) |
| T4 | `ChatGPT Image Nov 10, 2026, 04_01_13 PM.png` | Tile "violet operatory" | 1254 x 1254, RGB | 1,538,167 B | `17c73a7a5754d0e78e451b9fed29002b1ab3fd7d06d979ef66dbfc3ed18da321` | The operatory scene in violet on deep purple; mean (45, 31, 82) |

**Identification is my inference and needs the owner's confirmation:** the folder also holds two other 1774 x 887 logo-like images (`... 01_12_20 PM.png`, `... 01_15_22 PM.png`) and reference screenshots of other products (Epic screens, a WhatsApp background, a downloaded wallpaper). I chose `dental-keep-v01.png` as the logo (the newest of the three, named for the product) and the four 1254 x 1254 square images as the tiles. None of the third-party reference images is proposed for use.

**Repeatability (measured):** mean absolute RGB difference between opposite edges, compared with the image's own neighbouring-column difference:

| Tile | Left/right edge | Top/bottom edge | Neighbouring columns (baseline) | Reading |
|---|---|---|---|---|
| T1 | 2.1 | 2.8 | 1.8 | seamless |
| T2 | 5.3 | 4.2 | 5.9 | seamless |
| T3 | 2.2 | **8.9** | 2.0 | left/right seamless; a faint top/bottom seam is possible: check visually when the asset is prepared |
| T4 | 1.5 | 4.5 | 12.7 | seamless |

**Source and licence status:** all five are AI-generated images the owner produced with ChatGPT on 2026-11-10 (file names and dates). Under the provider's terms the output belongs to the user, but this briefing cannot verify that. **Owner confirmation required (decision D-2):** the owner holds the rights to use all five in a distributed product, and none was generated from a third-party image as input. The folder's `whatsapp-background.png` (saved 10:08) predates T1 (10:41), and T1's doodle style is similar. If T1 was made from that image, T1 should be dropped or regenerated without it.

**Weight:** the tiles are 1.5-1.9 MB each, too heavy for a sign-in page. Proposed preparation (implementation step, owner-approved assets only): scale each tile to a repeat unit of 627 x 627 (half size, still sharp at 2x density) and encode as WebP and PNG fallback, target under 250 KB each; keep the logo's aspect ratio (2:1), crop to the ink area plus margin, export at 2x of its display size (display about 280 x 140 CSS px), target under 60 KB. No stretching: tiles repeat at their natural size (`background-repeat: repeat`), and the logo keeps `object-fit: contain` with fixed aspect ratio.

**Location in the repository (proposed):** prepared derivatives in `src/alveara-client/src/assets/login/` (bundled by Vite with content-hashed names, served from the same origin). The original high-resolution files are not committed; their SHA-256 values above are the provenance record, and the preparation commands are recorded in the handoff.

**Safe fallback:** each theme has a solid background colour equal to its tile's dominant colour, applied underneath the image. If the image fails to load, is blocked, or the stored theme id is unknown (for example a theme removed later), the page shows the default theme's solid colour and the card exactly as before. If the logo fails, the card's existing "Sign in" heading and the page title "DentalKeep" remain (the alt text is announced). In Windows high-contrast/forced-colours mode the browser drops background images; the card stays readable.

## 3. Current state (read from the repository at `db138ec`)

- `src/alveara-client/src/pages/LoginPage.tsx` + `LoginPage.css`: a `PageHeader` "Sign in" with description "Enter your username and password to access DentalKeep.", status banners (expired, server unavailable, server problem, invalid credentials, locked, disabled, rate-limited), the form. No logo, no background, no footer.
- `App.tsx` `RootLayout` renders the sign-in variant of `DisconnectedBanner` on `/login`, `/mfa-challenge`, `/reset-password` (ALV-N020). The saved app theme applies to the sign-in page (ALV-N020, F-156).
- Practice configuration: `PracticeSettings` singleton (name, phone, address, `RowVersion`), `GET/PUT api/config/practice` behind `[RequirePermission(Permission.ManagePracticeConfiguration)]` and `[RequireCsrfToken]`, audited through `ConfigurationWrite.Audit` (`ConfigurationUpdated`).
- Anonymous endpoints today: sign-in, CSRF token, health (no class-level `[Authorize]` on `AuthController`). The `AuthAttempts` rate-limit policy covers authentication attempts only.
- Chain stage 0 asserts the DentalKeep brand on the sign-in page and the signed-in shell (B-01, B-02); stage 1 covers sign-in, lockout-adjacent refusals, expiry, revocation and the outage case. These must stay green.

## 4. Design

### 4.1 Theme catalog (fixed allow-list)
A single shared definition of four identifiers, used by the server (validation) and the client (asset map):

| Id | Asset | Solid fallback | Card style |
|---|---|---|---|
| `teal-doodle` | T1 | `#042B30` | light card |
| `blue-doodle` | T2 | `#0D314E` | light card |
| `blue-operatory` | T3 | `#0B2A50` | light card |
| `violet-operatory` | T4 | `#2D1F52` | light card |

Default theme (decision D-4): proposed `blue-operatory` (calmest, fewest small shapes). An id not in the catalog is refused by the server (400 `theme_unknown`) and, if found in storage, rendered as the default.

### 4.2 Persistence (one migration; needs the reviewer's approval of this briefing)
New singleton table `LoginPresentationSettings`: `Id` (fixed), `ThemeId` nvarchar(40) not null (check: lower-case letters, digits and hyphens), `FooterNoticeText` nvarchar(300) null (null = use the deployment default), `ShowFooterNotice` bit not null default 1, `UpdatedAtUtc`, `RowVersion`. Seeded with the default theme, null text, show = 1. A separate table, not new columns on `PracticeSettings`, so a sign-in-screen change never conflicts with an edit of the practice name (each has its own concurrency token). Down migration on an empty or seeded table drops it; nothing else changes.

### 4.3 Deployment-default notice
The "initially deployed country-appropriate notice" is configuration, not code and not detection: `LoginPresentation:DefaultFooterNotice` in the deployment settings (environment variable `LoginPresentation__DefaultFooterNotice`), validated at start-up with the same rules as an edited notice (an invalid default is logged and treated as empty, never shown raw). The deployer edits it during or after deployment by changing that setting. An administrator's text overrides it; "Use deployment default" clears the override. No country detection (plan exclusion). The default text itself is decision D-5.

### 4.4 Server API
- `GET api/public/login-presentation` (anonymous, no cookie needed, `Cache-Control: no-store`): `{ "themeId": "...", "footerNotice": "..." | null }`. `footerNotice` is null when display is off or the effective text is empty: a disabled notice is not sent, so it cannot be rendered. Returns nothing about users, accounts or the practice. A short-lived in-process cache (for example 30 s) keeps it cheap; it has its own modest rate-limit partition so it cannot be used to load the database.
- `GET api/config/login-presentation` (configuration authority): the stored values, the effective default, the catalog, and `rowVersion`.
- `PUT api/config/login-presentation` (configuration authority + CSRF): `{ themeId, footerNoticeText | null, showFooterNotice, rowVersion }`. Stale version -> 409 with the existing conflict shape.
- Permission: the existing `ManagePracticeConfiguration` (decision D-6 if the reviewer prefers `ManageUsers`-level separation; the plan says "existing least-privilege configuration authority").
- Request bodies validated by the existing model-validation pattern; malformed input 400 before the service.

### 4.5 Notice text rules (server-enforced; the client mirrors them for messages only)
- Trimmed; empty after trimming = "no custom text" (null).
- 1-300 characters; at most 3 line breaks; no other control characters; no tabs.
- Plain text only. Proposed (decision D-7): also refuse `<` and `>` and any URL scheme or address (`://`, `www.`, `mailto:`), so a stored notice can never look like markup or a link. Even if allowed, rendering is a React text node (escaped) with `white-space: pre-line`; never `dangerouslySetInnerHTML`, never auto-linked.

### 4.6 Audit (existing `ConfigurationWrite.Audit`, actor and time recorded by the audit service)
- `LoginThemeChanged`: "Sign-in theme changed from `<old id>` to `<new id>`."
- `LoginFooterNoticeChanged`: display state before and after, and whether the text changed (custom/default, character count). The full notice text is not copied into the audit detail (plan: "without unnecessarily duplicating notice text"); the stored row is the current text.
- `LoginFooterNoticeReset`: custom text cleared back to the deployment default.
- No credentials, IP addresses or user-agent data.

### 4.7 Client
- `LoginPage` (and the MFA-challenge and reset-password pages, which share the layout) gets a `LoginLayout`: full-page background div (decorative: CSS background, `aria-hidden`, no alt), logo `<img alt="DentalKeep">` with fixed aspect ratio, the existing card with its unchanged content, the footer `<footer>` only when `footerNotice` is a non-empty string.
- The presentation fetch never blocks or delays sign-in: the page renders at once with the default theme and no footer; the fetched theme/notice is applied when it arrives. If the fetch fails (outage), the default theme stays and no footer is shown; the existing "Local server unavailable" sign-in banner is unchanged.
- Card contrast: the card uses the app's existing light/dark tokens (it follows the saved app theme, F-156); the background never sits behind text. A thin focus ring and the existing banners keep their current contrast. `prefers-reduced-motion`: there is no motion to reduce (no animation added).
- Narrow widths: the card spans the viewport minus the 16 px gutter; the logo scales down with its aspect ratio; no horizontal scroll.
- Practice configuration gets a "Sign-in screen" tab: four theme thumbnails as a radio group (keyboard-operable, each labelled), the notice text box with a live character count and the rule messages, the "Show footer notice on login screen" checkbox, "Use deployment default", Save with the existing unsaved-changes guard and conflict message, and a preview of the effective notice as plain text.

### 4.8 What stays exactly as it is
`AuthController` and `AccountService` sign-in, lockout, the `AuthAttempts` rate limit, the identical refusal for wrong password and unknown user, CSRF handling, session expiry/revocation/sign-out, MFA, the ALV-N020 outage and identity behavior, the page title and visible "DentalKeep" text (chain B-01/B-02). No tracking, analytics, fonts or images from other origins.

## 5. Security and privacy review
| Concern | Handling |
|---|---|
| New anonymous endpoint | Read-only, returns two values, no identifiers, `no-store`, cached and rate-limited; same response for everyone, so nothing to enumerate |
| Stored XSS through the notice | Plain-text storage and rendering (escaped text node), markup and URL refusal (D-7), length and control-character limits; tests insert `<script>` and `<img onerror>` payloads and assert they appear as literal text or are refused |
| Remote content / tracking | Assets bundled and same-origin; no external URL can be configured; theme ids are an allow-list |
| Uploads | None (plan exclusion) |
| Privilege | Writes require configuration authority + CSRF; FrontDesk/Dentist etc. get 403; anonymous gets 401 on the config endpoints |
| Concurrency | `RowVersion` on the new singleton; stale write 409, nothing changed |
| Audit integrity | Actor and time from the audit service; no credentials; no full notice text duplication |
| Availability | Presentation fetch failure never blocks sign-in |

## 6. Files expected to change (implementation, after approval)
Server: new `LoginPresentationSettings` entity + model configuration + migration; `LoginPresentationService` (validation, catalog, audit); `LoginPresentationController` (public GET) and two actions in `ConfigurationController` (or a small `LoginPresentationConfigController`); catalog constants; Program.cs registration and the rate-limit partition; deployment setting. Client: `LoginLayout` component and CSS; `LoginPage`, `MfaChallengePage`, reset-password page use it; `loginPresentationApi.ts`; theme catalog map; prepared assets under `src/assets/login/`; Practice configuration "Sign-in screen" tab. Tests listed in section 7. No change to `AuthController`, `AccountService`, `AuthContext`, `RouteGuards`.

## 7. Focused evidence plan (each run within the owner's 5-minute rule, or with explicit permission above it)
| Area | Tests (planned) |
|---|---|
| Server unit/service | validation table (empty, spaces only, 300/301 chars, 4 line breaks, tab, control char, `<`, `>`, `http://`, `www.`), unknown theme refused, reset to default, effective-default resolution, invalid deployment default treated as empty, audit entries (theme old/new; notice display/changed flags; no full text) |
| Server API | anonymous public GET shape and `no-store`; disabled notice absent from the payload; config GET/PUT: anonymous 401, FrontDesk/Dentist 403, OfficeManager and Admin 200; missing CSRF refused; stale version 409 with nothing changed; idempotent re-save |
| Schema/migration | table, check constraint and seed; up/down on a clone; registered in `RealMigrationInventory` |
| Client unit | `LoginLayout` per theme (class/background), unknown id -> default, image error -> solid fallback, footer plain text renders `<script>` literally, disabled -> no footer element in the DOM, fetch failure -> default theme and no footer, sign-in not delayed by a slow fetch; config tab: radio group keyboard, validation messages, conflict, unsaved-changes guard |
| Accessibility | axe on the sign-in page for each theme in light and dark app themes, at 1280 px and 375 px; keyboard-only sign-in (Tab order logo-less: username, password, Sign in, footer has no focusable element); 200 % zoom no horizontal scroll; forced-colours snapshot |
| Browser (mocked Playwright) | the four themes at desktop and narrow widths with screenshots for the handoff; error, loading and server-unavailable states over each theme |
| Real backend | `auth` walkthrough unchanged (12); chain stage 0 + stage 1 re-run (B-01/B-02 brand, 1.01-1.12, 13.05) to prove sign-in, refusals, expiry, revocation and outage are unchanged |
| Negative controls | markup rendered as HTML -> the XSS test fails; disabled notice still sent -> the payload test fails; permission check removed -> the 403 tests fail |

Broad regression: none (deferred to ALV-N028; the ledger gains this package's obligations: full suites, the walkthroughs, the chain).

## 8. Risks
| Risk | Mitigation |
|---|---|
| A busy background hurts readability | Solid card, background never behind text, contrast tested per theme; tiles are dark and low-contrast by nature |
| Heavy images slow the sign-in page | Prepared derivatives under 250 KB, lazy background, sign-in never waits for them |
| Brand inconsistency ("KEEP dental" vs "DentalKeep") | Decision D-1 before any asset is committed |
| Licence uncertainty | Decision D-2; nothing is committed until confirmed |
| The new anonymous endpoint becomes an attack surface | Read-only, two values, cached, rate-limited, no identifiers |
| T3 faint top/bottom seam | Visual check at preparation; owner may swap or regenerate |

## 9. Decisions needed before implementation
| # | Decision | Who | Proposal |
|---|---|---|---|
| D-1 | The logo reads "KEEP dental"; the product name is "DentalKeep". Use this logo as is, or supply one that reads "DentalKeep"? | owner | owner's call; nothing is changed in the artwork by the agent |
| D-2 | Confirm the rights to all five images and that none was generated from a third-party image (see the T1 note) | owner | confirm, or drop/regenerate T1 |
| D-3 | Logo background: show the opaque light-grey logo on a light panel above the card (no edit), or approve a transparent-background derivative | owner | light panel, no edit |
| D-4 | Default theme | owner | `blue-operatory` |
| D-5 | The deployment-default notice text (and which country it is written for) | owner | owner supplies the text |
| D-6 | Permission: existing `ManagePracticeConfiguration` | reviewer | keep the existing authority |
| D-7 | Refuse `<`, `>` and URLs in the notice text (in addition to plain-text rendering) | reviewer | refuse |
| D-8 | One migration (new singleton table, section 4.2) | reviewer | approve |

## 10. Method and limits of this briefing
Read-only: the amended plan, the current login, configuration, permission and rate-limit code, and the five image files (dimensions, alpha, edge seams and SHA-256 measured with Python/PIL; images viewed). No file was copied into the repository, no code or test was changed, and no test was run. The asset roles and the licence status are inferences from file names, dates and content until the owner confirms D-1 to D-3.
