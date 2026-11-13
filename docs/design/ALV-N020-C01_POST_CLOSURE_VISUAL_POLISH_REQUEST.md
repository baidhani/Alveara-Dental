# ALV-N020-C01: post-closure visual polish request (three CSS changes)

- **Requested by:** the product owner, after trying the finished sign-in screen on the dev stack (2026-11-12), with two reference screenshots of the legacy sign-in screens.
- **Status of the story:** ALV-N020-C01 is `COMPLETE` (R02 approved, closure `f2bef59`, unpushed). Nothing has been changed since. This request asks the reviewer to authorize a narrow follow-up, so a COMPLETE story is not altered without a ruling.
- **Nature:** presentation only. CSS plus browser-test assertions. No server, database, migration, dependency, asset or permission change. No change to what the screen says or does.
- **Regression:** focused proof only; `fullRegression` stays `DEFERRED_TO_ALV-N028`; not release-certified.

## What the owner observed

1. The DentalKeep logo sits too close to the top of the panel, and the "Sign in" heading and form sit too close to the bottom of the logo. There is a large unused gap below the Sign in button (it is where the footer notice is pushed to the bottom), so space can be taken from there.
2. The Sign in button is wider than the Username and Password fields.
3. The footer notice text is the same size as the field labels. In the two legacy reference screens the notice is clearly smaller than the field text.

## Findings (read from the code, not changed)

| # | Cause | Where |
|---|---|---|
| 2 | The sign-in form is a vertical flex container, so the button stretches to the form's full width (420 px); the two inputs are given no width and keep the browser's default input width (about 375 px). This mismatch existed before ALV-N020-C01 (`LoginPage.css` was not touched by it); the new full-height panel only made it noticeable. | `src/alveara-client/src/pages/LoginPage.css`, shared `FormField` |
| 1 | Space above the logo is the panel top padding (`--space-6`, 24 px; 18 px in compact density) plus the logo's top margin (also `--space-6`): about 48 px. Space below the logo is `--space-4` (16 px; 12 px compact). | `src/alveara-client/src/pages/login/LoginLayout.css` |
| 3 | The notice uses `--font-size-sm` (0.8125rem = 13 px), the same token as the field labels (`.alv-form-field__label`). | `LoginLayout.css` |

## Proposed changes

**P-1. Fields and button the same width.** Make the Username and Password inputs fill the form width (the same 420 px as the button), scoped to the sign-in form only (a selector under the login form), so the shared `FormField` is not changed for any other page. Alternative the reviewer may prefer: leave the inputs and cap the button to their width. Recommendation: fill the width (one consistent column, and it is the larger touch target).

**P-2. More space above the logo and between the logo and the form.** Logo top margin 24 px -> 48 px (about 72 px from the top of the panel); logo bottom margin 16 px -> 40 px. Compact density scales with the existing tokens. The space comes out of the empty area above the notice; the notice stays pinned to the bottom. At 375 px wide and in the 200%-zoom viewports (640x400, 960x540) the top spacing is reduced proportionally (for example 24 px above, 24 px below) so no extra scrolling is introduced, and the existing "no horizontal scroll; Sign in and footer reachable and not obscured" assertions keep passing.

**P-3. Smaller footer notice.** Notice font size from 13 px to **11 px** (`0.6875rem`), line height about 1.45, same colour tokens. Measured on the two reference screenshots the notice is about 60% of the field text size; 11 px is about 73% of the 15 px field text and is clearly smaller than the 13 px labels. Alternative if the reviewer sets a readability floor of 12 px (`--font-size-xs`): 12 px, which is only a small step below the 13 px labels. Recommendation: 11 px. The text is unchanged and still a plain text node; contrast is unchanged (same colour tokens, re-checked by axe in light and dark).

## Proof proposed (focused, standing five-minute permission)

- New real-browser assertions (extend `e2e/login-presentation-access.spec.ts` or a small new spec), desktop project: at 1280x800, 375x812 and the two 200%-zoom viewports - each input's rendered width equals the Sign in button's width; the gap from the panel top to the logo and from the logo to the "Sign in" heading is at least the proposed values (or the proportional values at the small viewports); the notice's computed font size is smaller than the field label's.
- Re-run: `login-presentation.spec.ts` (12, axe in both themes at 1280 and 375), `login-presentation-access.spec.ts` (6, zoom and forced colours), shell + login-banner (28); `LoginLayout.test.tsx`; `npx tsc -b`; lint count unchanged (42).
- Screenshots of the five backgrounds before/after for the owner and the reviewer.
- Negative control: revert each CSS rule and show its assertion fails.

## Scope and limits

Only the three items above. No wording, label, logo, background, permission, endpoint or database change; F-075 and the ALV-N029 rotation stay where they are.

## Decisions requested from the reviewer

1. May this be done as a narrow follow-up attempt on ALV-N020-C01 (R03: CSS and tests only, closure again by review-closure protocol), or should it be a separate polish item (for example folded into the UI-polish work with the other visual notes)?
2. P-1: fill the field width (recommended) or cap the button?
3. P-3: 11 px (recommended) or 12 px?
