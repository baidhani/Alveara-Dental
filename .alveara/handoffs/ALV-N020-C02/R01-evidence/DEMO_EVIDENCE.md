# ALV-N020-C02 R01: demonstration evidence

Before/after screenshots (mocked sign-in, real Chromium, light theme, notice shown) in `artifacts/focused-proof/screenshots/`:

- `before/` and `after/`: `teal-doodle`, `blue-doodle`, `blue-operatory`, `violet-operatory`, `neon-doodle` at 1280x800 (all five bundled backgrounds), plus `teal-doodle-375x812.png`, `teal-doodle-640x400-zoom200.png` and `teal-doodle-960x540-zoom200.png`.

Observed: before, Username/Password were 360 px and Sign in 420 px, the logo sat about 48 px from the top and 16 px above the heading, the notice was 13 px; after, one 420 px column, logo 72 px from the top with 40 px below it, 12 px centred notice, the owner's SVG logo. At 375x812 and 640x400 the spacing contracts and the form, error area and footer remain in view. The owner can see the result on their own dev stack after rebuilding the client (`npm run build` is not needed for the Vite dev client; a refresh shows it).
