# ALV-N020-C02 pre-implementation brief: DentalKeep login layout visual polish

- **Plan item:** 57b (inserted after ALV-N020-C01, before ALV-N021).
- **Authority:** `docs/design/ALV-N020-C01_POST_CLOSURE_VISUAL_POLISH_REVIEW.md` (decision 2026-11-12: APPROVED AS A SEPARATE COMPANION STORY). ALV-N020-C01 stays COMPLETE at R02 and is not reopened.
- **Status:** brief submitted for independent approval. **No CSS, test, asset or application file has been changed and no test has been run.** Implementation starts only after this brief is approved.
- **Regression:** focused proof only; `fullRegression` will be `DEFERRED_TO_ALV-N028`; not release-certified. The standing test-permission policy applies (a focused group expected at 5 minutes or less runs without asking; anything longer is asked first).

## 1. The owner's observation (verbatim from the request)

Reproduced from `docs/design/ALV-N020-C01_POST_CLOSURE_VISUAL_POLISH_REQUEST.md`, "What the owner observed", made after the owner tried the finished sign-in screen on the dev stack (2026-11-12) and supplied two screenshots of the legacy sign-in screens:

1. The DentalKeep logo sits too close to the top of the panel, and the "Sign in" heading and form sit too close to the bottom of the logo. There is a large unused gap below the Sign in button (it is where the footer notice is pushed to the bottom), so space can be taken from there.
2. The Sign in button is wider than the Username and Password fields.
3. The footer notice text is the same size as the field labels. In the two legacy reference screens the notice is clearly smaller than the field text.

The owner's words: "the dental keep logo is very close to the top of the page and needs more space at the top while the sign in text and fields are very close to the logo at the bottom of the logo and needs more space. there is plenty of space at the bottom of the sign button that we can borrow some of it. the sign button is longer than the username and password fields ... needs to be fixed." and "the font of the hipaa notice ... needs to be smaller font right now it is the same size as the labels of the fields."

## 2. Findings from the current code (read only)

| # | Cause | Where |
|---|---|---|
| Field width | The sign-in form is a vertical flex container, so the button stretches to the form column (420 px); the two inputs have no width and keep the browser default (about 375 px). This predates C01 (`LoginPage.css` was not touched by it). | `src/alveara-client/src/pages/LoginPage.css`; shared `components/FormField` |
| Logo spacing | Panel padding-top `--space-6` plus logo margin-top `--space-6` is about 48 px above the logo; logo margin-bottom is `--space-4` (16 px). At 480 px or narrower the panel top padding drops to `--space-4`. | `pages/login/LoginLayout.css` lines 16-43, 65-70 |
| Notice size | The notice uses `--font-size-sm` (13 px), the same token as the field labels. | `LoginLayout.css` line 55 |
| Logo file | The logo is a 280x126 raster `assets/login/dentalkeep-logo.png` (57,070 bytes), imported in `LoginLayout.tsx` line 3 and rendered with `alt="DentalKeep"`, `width={280} height={126}`; CSS fixes `aspect-ratio: 280 / 126` and `width: min(280px, 70vw)`. | `LoginLayout.tsx` line 56; `LoginLayout.css` lines 36-43 |

## 3. SVG logo inventory

| Item | Value |
|---|---|
| File | `C:\Users\firas\Desktop\Epic\dentalkeep-logo-v4.svg` (owner supplied) |
| SHA-256 | `9CECF4A6C72A91FC7C8CF225805CA7DFD756F61760EE6FE2434F1C2E76ED82C0` (recomputed 2026-11-12; matches the review) |
| Size | 5,213 bytes |
| viewBox | `0 0 1774 820` (ratio 2.1634; the PNG slot is 280/126 = 2.2222, so the slot proportions must change) |
| Elements | `svg`, `title` (id `title`, text "dentalKeep logo"), `defs`, `g`, 4 `path`, 2 `use` |
| Active content | None. No script, event-handler attribute, `style` element, `foreignObject`, `image`, `@font-face`, `data:` URI or external URL. The only references are the two `use href="#tooth-e"` pointing at a definition inside the same file. |
| Licence/source | Supplied by the product owner as the DentalKeep logo (the same owner-supplied identity as C01's raster); no third-party asset. Recorded as owner-supplied in the evidence. |
| Accessible name | The file carries its own `role="img"` and `title`, but it is used through an `<img>` element, where only the element's `alt` counts. The accessible name stays `alt="DentalKeep"` exactly as in C01. |

Handling: copy the file unchanged (byte-identical, hash re-checked in a test) to `src/alveara-client/src/assets/login/dentalkeep-logo.svg`, import it the same way the PNG is imported (Vite bundles it locally; no remote retrieval), render it through the existing `<img>` with the same `alt`. The old PNG is removed from the login layout only. Because it is used as an `<img>`, the browser does not run any script in it even if one were present, and the file has none. The attributes `width`/`height` become `1774`/`820` ratio-matched values (for example `width={280} height={129}`, 280 x 820/1774 = 129.4) so the browser reserves the correct box before load, and the CSS `aspect-ratio` becomes `1774 / 820`.

## 4. Proposed changes

Values below are the starting point; if a screenshot shows a value is off, the evidence will say what was adjusted and why, inside the approved bounds.

**P-1. One equal-width column.** Under `.alv-signin__content` only (the login form's container), make the form-field inputs `width: 100%` with `box-sizing: border-box`, so Username, Password and Sign in share the 420 px column. Selector scoped under `.alv-signin__content`; `FormField` and `LoginPage.css` are not changed. The MFA challenge and password-reset pages use the same layout and receive the same alignment through the same scoped rule (their inputs and buttons, if any, share the column), which is the consistent behaviour wanted. No other page uses `.alv-signin__content`.

**P-2. Balanced spacing.** Logo margin-top from `--space-6` to about 48 px (about 72 px from the panel top at desktop) and margin-bottom from `--space-4` to about 40 px, taken from the space above the pinned footer. At 480 px wide and below, and in the 200%-equivalent viewports (640x400 of 1280x800, 960x540 of 1920x1080), the margins contract (for example 16 px above and 24 px below) before anything may clip, overlap, scroll horizontally or become unreachable. The notice keeps `margin-top: auto`, so it stays at the bottom when there is room and follows the content when there is not.

**P-3. 12 px footer notice.** `font-size: var(--font-size-xs)` (0.75rem = 12 px, per the review), `line-height: 1.5`, same colour tokens. The text stays a plain text node; wording, enabled/disabled behaviour and configuration are untouched.

**P-4. Logo substitution** as in section 3, including `aspect-ratio: 1774 / 820`, `object-fit: contain`, and `height: auto`.

## 5. Out of scope (unchanged)

Wording and notice content or configuration, other logos and the five bundled backgrounds, routes and endpoints, permissions, server behaviour, upload and backup handling, schema and migrations, dependencies, authentication and session security, the shared `FormField`, F-075 (ALV-N027) and rotation (ALV-N029). No `package.json` or lockfile change; no new test infrastructure.

## 6. Failure-first notes

- If the SVG fails to load, the `<img>` shows its `alt` text "DentalKeep" in the reserved box (width and height attributes keep the layout from jumping); sign-in is unaffected.
- If a very narrow or 200% viewport cannot fit the spacing, the spacing shrinks first; vertical scrolling of the panel is allowed, horizontal scrolling and unreachable content are not.
- Forced colours: the SVG is an `<img>` and keeps its own colours (the logo is artwork, not decoration). The backdrop and panel edge rules from C01 R02 stay. The notice keeps system text colour.
- Not handled by this story: a logo with a different aspect ratio supplied later (the ratio is asserted from the file, so a different file fails the test loudly rather than distorting).

## 7. Proof plan (focused, real browser, desktop project)

Extend `e2e/login-presentation-access.spec.ts` or add one small spec (`login-layout-polish.spec.ts`); keep existing assertions.

| Assertion | Viewports | Negative control (revert the rule, assertion must fail) |
|---|---|---|
| Username, Password and Sign in have equal rendered width | 1280x800, 375x812, 640x400, 960x540 | remove the scoped `width: 100%` |
| Logo top gap and logo-to-form gap are at least the desktop values (1280x800) and at least the contracted minimums elsewhere, with no horizontal scroll and Sign in and the footer reachable and unobscured (`elementFromPoint`) | same four | restore the old margins |
| Notice computed font size is 12 px and smaller than the field label's (13 px); contrast via axe in light and dark; forced-colours run still passes | 1280x800 (both themes), forced colours | restore `--font-size-sm` |
| Logo `<img>`: `alt="DentalKeep"`, loaded (`naturalWidth > 0`), rendered width/height ratio within 0.5% of 1774/820 (no distortion), no clipping (box inside the panel), source is a bundled local `.svg` with no network request to any other origin | all four plus forced colours | restore the PNG slot ratio (280/126) with the SVG |
| The bundled SVG file hash equals `9CECF4A6...82C0` and contains no `<script`, event attribute, external reference | unit test (Vitest) | alter the file |

Also re-run, expected about 1 to 2 minutes each: `login-presentation.spec.ts` (12), `login-presentation-access.spec.ts` (6 plus new), `login-banner-contrast.spec.ts` and the shell specs (28 together); `LoginLayout.test.tsx`; `npx tsc -b`; lint count (42 expected unchanged, with and without these files). Route and expected/actual duration go into the evidence.

Screenshots: before (captured from the current build, before any change) and after, for each of the five bundled backgrounds at 1280x800, so the owner can compare; plus narrow (375x812) and 200%-equivalent for the default background.

## 8. Delivery

Implementation attempt R01 of ALV-N020-C02 (Stage 1 package under the deferred-regression route: handoff, evidence, ZIP), then independent review and the closure protocol. PROGRESS.md entries are stamped with session CC-20261109-8w3d. Nothing is pushed without the owner's say-so.

## 9. Assumptions logged

1. 12 px is `--font-size-xs` (confirmed: `styles/tokens.css` line 47, `0.75rem`).
2. The exact pixel values in P-2 are starting values within the approved intent; screenshots decide any adjustment.
3. The scoped width rule also applies to the MFA and password-reset pages because they share `.alv-signin__content`; this is consistent with the request and is covered by the same assertions where those pages have fields.
